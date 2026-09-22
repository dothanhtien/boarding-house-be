using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class OrganizationSettingsControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "actor@test.com";
    private const string ActorPassword = "password123";

    private Guid _actorId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response)
    {
        var setCookieHeader = response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal));

        return setCookieHeader.Split(';')[0]["accessToken=".Length..];
    }

    private async Task AuthenticateAsync()
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = ActorEmail,
            Phone = "0900000099",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Actor User"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        _actorId = actor!.Id;

        await factory.GrantPlatformAdminRoleAsync(actor.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ActorEmail,
            Password = ActorPassword
        });

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
    }

    private async Task<Guid> CreateOrganizationAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = "Test Organization",
            OwnerId = _actorId
        });
        var organization = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;
        return organization!.Id;
    }

    private async Task<Guid> GetRoleIdBySlugAsync(string slug)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await context.Roles.SingleAsync(r => r.Slug == slug);

        return role.Id;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync(
        string email, string phone, Func<Guid, Task>? beforeLogin = null)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = phone,
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Test User"
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        if (beforeLogin is not null)
        {
            await beforeLogin(user!.Id);
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = ActorPassword });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        return (client, user!.Id);
    }

    private async Task AddMemberAsync(Guid organizationId, Guid userId, string roleSlug)
    {
        var roleId = await GetRoleIdBySlugAsync(roleSlug);
        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members", new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_NeverUpdated_ReturnsDefaults()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.GetAsync($"/api/organizations/{organizationId}/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationSettingsResponse>>(JsonOptions))?.Data;
        Assert.Equal(organizationId, settings!.OrganizationId);
        Assert.Null(settings.VatRate);
        Assert.Equal("VND", settings.Currency);
        Assert.Null(settings.LateFeeGraceDays);
        Assert.Null(settings.LateFeeType);
        Assert.Null(settings.DefaultBillingDay);
    }

    [Fact]
    public async Task UpdateSettings_FirstPut_CreatesAndReturnsUpdatedValues()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            VatRate = 8,
            DefaultBillingDay = 5,
            LateFeeType = LateFeeType.Fixed,
            LateFeeValue = 50000,
            LateFeeGraceDays = 3
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationSettingsResponse>>(JsonOptions))?.Data;
        Assert.Equal(8, updated!.VatRate);
        Assert.Equal(5, updated.DefaultBillingDay);
        Assert.Equal(LateFeeType.Fixed, updated.LateFeeType);
        Assert.Equal(50000, updated.LateFeeValue);
        Assert.Equal(3, updated.LateFeeGraceDays);

        var getResponse = await _client.GetAsync($"/api/organizations/{organizationId}/settings");
        var fetched = (await getResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationSettingsResponse>>(JsonOptions))?.Data;
        Assert.Equal(8, fetched!.VatRate);
        Assert.Equal(LateFeeType.Fixed, fetched.LateFeeType);
    }

    [Fact]
    public async Task UpdateSettings_SecondPut_UpdatesExistingRowWithoutConflict()
    {
        var organizationId = await CreateOrganizationAsync();

        await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest { VatRate = 8 });
        var secondResponse = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest { VatRate = 10 });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var updated = (await secondResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationSettingsResponse>>(JsonOptions))?.Data;
        Assert.Equal(10, updated!.VatRate);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateSettings_LateFeeTypeWithoutValue_Returns400()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            LateFeeType = LateFeeType.Fixed
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_DefaultBillingDayOutOfRange_Returns400()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            DefaultBillingDay = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_DefaultBillingDayAtBoundaries_Returns200()
    {
        var organizationId = await CreateOrganizationAsync();

        var firstResponse = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            DefaultBillingDay = 1
        });
        var lastResponse = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            DefaultBillingDay = 28
        });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lastResponse.StatusCode);
    }

    [Fact]
    public async Task GetSettings_UnknownOrganizationId_Returns404()
    {
        var response = await _client.GetAsync($"/api/organizations/{Guid.NewGuid()}/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_UnknownOrganizationId_Returns404()
    {
        var response = await _client.PatchAsJsonAsync($"/api/organizations/{Guid.NewGuid()}/settings", new UpdateOrganizationSettingsRequest { VatRate = 8 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_EmptyBody_KeepsFieldsUnchanged()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_OnlyOneField_KeepsOtherFieldsUnchanged()
    {
        var organizationId = await CreateOrganizationAsync();

        await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            VatRate = 8,
            DefaultBillingDay = 5
        });

        var response = await _client.PatchAsJsonAsync($"/api/organizations/{organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            VatRate = 10
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationSettingsResponse>>(JsonOptions))?.Data;
        Assert.Equal(10, updated!.VatRate);
        Assert.Equal(5, updated.DefaultBillingDay);
    }

    [Fact]
    public async Task GetSettings_WithoutAuthentication_Returns401()
    {
        var organizationId = await CreateOrganizationAsync();
        using var unauthenticatedClient = factory.CreateClient();

        var response = await unauthenticatedClient.GetAsync($"/api/organizations/{organizationId}/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_AuthenticatedWithoutOrganizationSettingReadPermission_Returns403()
    {
        var organizationId = await CreateOrganizationAsync();

        using var unprivilegedClient = factory.CreateClient();
        var registerResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "no-permission@test.com",
            Phone = "0900000098",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "No Permission User"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "no-permission@test.com",
            Password = ActorPassword
        });
        unprivilegedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        var response = await unprivilegedClient.GetAsync($"/api/organizations/{organizationId}/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_UserIsMemberOfDifferentOrganization_Returns404()
    {
        var targetOrganizationId = await CreateOrganizationAsync();
        var foreignOrganizationId = await CreateOrganizationAsync();

        var (memberClient, memberId) = await CreateAuthenticatedClientAsync("foreign-settings-member@test.com", "0900000120");
        await AddMemberAsync(foreignOrganizationId, memberId, "organization_admin");

        var response = await memberClient.GetAsync($"/api/organizations/{targetOrganizationId}/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_UserIsMemberOfSameOrganization_Returns200()
    {
        var targetOrganizationId = await CreateOrganizationAsync();

        var (memberClient, memberId) = await CreateAuthenticatedClientAsync("settings-member@test.com", "0900000121");
        await AddMemberAsync(targetOrganizationId, memberId, "organization_staff");

        var response = await memberClient.GetAsync($"/api/organizations/{targetOrganizationId}/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_UserIsMemberOfDifferentOrganization_Returns404()
    {
        var targetOrganizationId = await CreateOrganizationAsync();
        var foreignOrganizationId = await CreateOrganizationAsync();

        var (memberClient, memberId) = await CreateAuthenticatedClientAsync("foreign-settings-updater@test.com", "0900000122");
        await AddMemberAsync(foreignOrganizationId, memberId, "organization_admin");

        var response = await memberClient.PatchAsJsonAsync(
            $"/api/organizations/{targetOrganizationId}/settings", new UpdateOrganizationSettingsRequest { VatRate = 8 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_PlatformAdminWhoIsNotAMember_Returns200()
    {
        var targetOrganizationId = await CreateOrganizationAsync();

        var (adminClient, _) = await CreateAuthenticatedClientAsync(
            "other-settings-admin@test.com", "0900000123", userId => factory.GrantPlatformAdminRoleAsync(userId));

        var response = await adminClient.PatchAsJsonAsync(
            $"/api/organizations/{targetOrganizationId}/settings", new UpdateOrganizationSettingsRequest { VatRate = 8 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
