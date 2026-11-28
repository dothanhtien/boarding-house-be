using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Services.Security;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class OrganizationPaymentGatewaysControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string Password = "password123";
    private const string Secret = "TESTSECRET0123456789";

    private Guid _actorId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();

        var (client, actorId) = await CreateAuthenticatedClientAsync(
            "actor@test.com", "0900000099", userId => factory.GrantPlatformAdminRoleAsync(userId));
        _client.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        _actorId = actorId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal))
            .Split(';')[0]["accessToken=".Length..];

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync(
        string email, string phone, Func<Guid, Task>? beforeLogin = null)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = phone,
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Test User"
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        if (beforeLogin is not null)
        {
            await beforeLogin(user.Id);
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return (client, user.Id);
    }

    private async Task<Guid> CreateOrganizationAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = "Test Organization",
            OwnerId = _actorId
        });
        return (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())!.Data!.Id;
    }

    private async Task AddMemberAsync(Guid organizationId, Guid userId, string roleSlug)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleId = await context.Roles.Where(r => r.Slug == roleSlug).Select(r => r.Id).SingleAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members", new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> PutVnPayAsync(Guid organizationId, object body, HttpClient? client = null) =>
        (client ?? _client).PutAsJsonAsync($"/api/organizations/{organizationId}/payment-gateways/vnpay", body);

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    private async Task<string> ReadSavedSecretAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.OrganizationPaymentGateways.SingleAsync(g => g.OrganizationId == organizationId);
        return scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect(saved.SecretEncrypted);
    }

    private async Task<int> CountAccountsAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.OrganizationPaymentGateways.CountAsync(g => g.OrganizationId == organizationId);
    }

    [Fact]
    public async Task Put_FirstTime_CreatesTheAccountWithAnEncryptedSecret()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await PutVnPayAsync(organizationId, new { merchantCode = " TESTCODE ", secret = Secret, isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);

        var account = await ReadDataAsync<OrganizationPaymentGatewayResponse>(response);
        Assert.Equal((organizationId, PaymentGateway.VnPay, "TESTCODE", true),
            (account.OrganizationId, account.Gateway, account.MerchantCode, account.IsActive));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.OrganizationPaymentGateways.SingleAsync(g => g.Id == account.Id);
        Assert.NotEqual(Secret, saved.SecretEncrypted);
        Assert.DoesNotContain(Secret, saved.SecretEncrypted);
        Assert.Equal(Secret, scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect(saved.SecretEncrypted));
        Assert.Equal(_actorId, saved.CreatedBy);
    }

    [Fact]
    public async Task Put_FirstTime_PersistsTheDataProtectionKeyInPostgres()
    {
        var organizationId = await CreateOrganizationAsync();

        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keyCount = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM data_protection_keys")
            .SingleAsync();
        Assert.True(keyCount >= 1);
    }

    [Fact]
    public async Task Put_SecretWithSurroundingSpaces_IsSavedTrimmed()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = $"  {Secret}  ", isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Secret, await ReadSavedSecretAsync(organizationId));
    }

    [Fact]
    public async Task Put_FirstTimeWithoutSecret_Returns400AndSavesNothing()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Secret is required", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountAccountsAsync(organizationId));
    }

    [Fact]
    public async Task Put_WithoutIsActive_Returns400()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAccountsAsync(organizationId));
    }

    [Fact]
    public async Task Put_UpdateWithoutSecret_KeepsTheSavedSecret()
    {
        var organizationId = await CreateOrganizationAsync();
        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        var response = await PutVnPayAsync(organizationId, new { merchantCode = "OTHERCODE", isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await ReadDataAsync<OrganizationPaymentGatewayResponse>(response);
        Assert.Equal(("OTHERCODE", false), (account.MerchantCode, account.IsActive));
        Assert.NotNull(account.UpdatedAt);
        Assert.Equal(Secret, await ReadSavedSecretAsync(organizationId));
    }

    [Fact]
    public async Task Put_UpdateWithNewSecret_ReplacesItWithoutAddingARow()
    {
        var organizationId = await CreateOrganizationAsync();
        var first = await ReadDataAsync<OrganizationPaymentGatewayResponse>(
            await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true }));

        var response = await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = "NEWSECRET9876543210", isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await ReadDataAsync<OrganizationPaymentGatewayResponse>(response);
        Assert.Equal(first.Id, account.Id);
        Assert.Equal(1, await CountAccountsAsync(organizationId));
        Assert.Equal("NEWSECRET9876543210", await ReadSavedSecretAsync(organizationId));
    }

    [Fact]
    public async Task Put_UpdateWithNewSecret_KeepsTheReplacedCredentialsWithWhoReplacedThem()
    {
        var organizationId = await CreateOrganizationAsync();
        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = "NEWSECRET9876543210", isActive = true });
        await PutVnPayAsync(organizationId, new { merchantCode = "OTHERCODE", isActive = true });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var account = await context.OrganizationPaymentGateways.SingleAsync(g => g.OrganizationId == organizationId);
        var retired = await context.OrganizationPaymentGatewayCredentialHistories
            .Where(h => h.OrganizationPaymentGatewayId == account.Id)
            .OrderBy(h => h.Version)
            .ToListAsync();
        Assert.Equal(3, account.CredentialsVersion);
        Assert.Equal(
            [(1, "TESTCODE", Secret, _actorId), (2, "TESTCODE", "NEWSECRET9876543210", _actorId)],
            retired.Select(h => (h.Version, h.MerchantCode, protector.Unprotect(h.SecretEncrypted), h.ReplacedBy)));
    }

    [Fact]
    public async Task Put_UpdateWithTheSameValues_KeepsTheCredentialsVersion()
    {
        var organizationId = await CreateOrganizationAsync();
        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", isActive = false });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.OrganizationPaymentGateways.Where(g => g.OrganizationId == organizationId).Select(g => g.CredentialsVersion).SingleAsync());
        Assert.Empty(await context.OrganizationPaymentGatewayCredentialHistories.ToListAsync());
    }

    [Fact]
    public async Task Put_GatewayInRouteIsCaseInsensitive()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/payment-gateways/VNPAY",
            new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentGateway.VnPay, (await ReadDataAsync<OrganizationPaymentGatewayResponse>(response)).Gateway);
    }

    [Theory]
    [InlineData("paypal")]
    [InlineData("99")]
    public async Task Put_UnknownGateway_Returns400(string gateway)
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/payment-gateways/{gateway}",
            new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAccountsAsync(organizationId));
    }

    [Fact]
    public async Task Put_UnknownOrganization_Returns404()
    {
        var response = await PutVnPayAsync(Guid.NewGuid(), new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutAuthentication_Returns401()
    {
        var organizationId = await CreateOrganizationAsync();
        using var anonymousClient = factory.CreateClient();

        var response = await PutVnPayAsync(
            organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true }, anonymousClient);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_OrganizationStaff_Returns403()
    {
        var organizationId = await CreateOrganizationAsync();
        var (staffClient, staffId) = await CreateAuthenticatedClientAsync("staff@test.com", "0900000098");
        await AddMemberAsync(organizationId, staffId, "organization_staff");

        var response = await PutVnPayAsync(
            organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true }, staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_OrganizationAdminOfThisOrganization_Returns200()
    {
        var organizationId = await CreateOrganizationAsync();
        var (adminClient, adminId) = await CreateAuthenticatedClientAsync("org-admin@test.com", "0900000097");
        await AddMemberAsync(organizationId, adminId, "organization_admin");

        var response = await PutVnPayAsync(
            organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true }, adminClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.OrganizationPaymentGateways.SingleAsync(g => g.OrganizationId == organizationId);
        Assert.Equal(adminId, saved.CreatedBy);
    }

    [Fact]
    public async Task Put_OrganizationAdminOfAnotherOrganization_Returns404AndSavesNothing()
    {
        var targetOrganizationId = await CreateOrganizationAsync();
        var foreignOrganizationId = await CreateOrganizationAsync();
        var (adminClient, adminId) = await CreateAuthenticatedClientAsync("foreign-admin@test.com", "0900000096");
        await AddMemberAsync(foreignOrganizationId, adminId, "organization_admin");

        var response = await PutVnPayAsync(
            targetOrganizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true }, adminClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountAccountsAsync(targetOrganizationId));
    }

    [Fact]
    public async Task GetAll_ListsTheAccountsWithoutSecrets()
    {
        var organizationId = await CreateOrganizationAsync();
        await PutVnPayAsync(organizationId, new { merchantCode = "TESTCODE", secret = Secret, isActive = true });

        var response = await _client.GetAsync($"/api/organizations/{organizationId}/payment-gateways");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        var account = Assert.Single(await ReadDataAsync<List<OrganizationPaymentGatewayResponse>>(response));
        Assert.Equal((PaymentGateway.VnPay, "TESTCODE", true), (account.Gateway, account.MerchantCode, account.IsActive));
    }

    [Fact]
    public async Task GetAll_NothingConfigured_ReturnsAnEmptyList()
    {
        var organizationId = await CreateOrganizationAsync();

        var response = await _client.GetAsync($"/api/organizations/{organizationId}/payment-gateways");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadDataAsync<List<OrganizationPaymentGatewayResponse>>(response));
    }

    [Fact]
    public async Task GetAll_OnlyListsTheRequestedOrganization()
    {
        var organizationId = await CreateOrganizationAsync();
        var otherOrganizationId = await CreateOrganizationAsync();
        await PutVnPayAsync(otherOrganizationId, new { merchantCode = "OTHERCODE", secret = Secret, isActive = true });

        var response = await _client.GetAsync($"/api/organizations/{organizationId}/payment-gateways");

        Assert.Empty(await ReadDataAsync<List<OrganizationPaymentGatewayResponse>>(response));
    }

    [Fact]
    public async Task GetAll_UnknownOrganization_Returns404()
    {
        var response = await _client.GetAsync($"/api/organizations/{Guid.NewGuid()}/payment-gateways");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithoutAuthentication_Returns401()
    {
        var organizationId = await CreateOrganizationAsync();
        using var anonymousClient = factory.CreateClient();

        var response = await anonymousClient.GetAsync($"/api/organizations/{organizationId}/payment-gateways");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_UserWithoutAnyRole_Returns403()
    {
        var organizationId = await CreateOrganizationAsync();
        var (client, _) = await CreateAuthenticatedClientAsync("no-role@test.com", "0900000095");

        var response = await client.GetAsync($"/api/organizations/{organizationId}/payment-gateways");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_OrganizationStaff_CanRead()
    {
        var organizationId = await CreateOrganizationAsync();
        var (staffClient, staffId) = await CreateAuthenticatedClientAsync("staff@test.com", "0900000098");
        await AddMemberAsync(organizationId, staffId, "organization_staff");

        var response = await staffClient.GetAsync($"/api/organizations/{organizationId}/payment-gateways");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_MemberOfAnotherOrganization_Returns404()
    {
        var targetOrganizationId = await CreateOrganizationAsync();
        var foreignOrganizationId = await CreateOrganizationAsync();
        var (memberClient, memberId) = await CreateAuthenticatedClientAsync("foreign-staff@test.com", "0900000094");
        await AddMemberAsync(foreignOrganizationId, memberId, "organization_admin");

        var response = await memberClient.GetAsync($"/api/organizations/{targetOrganizationId}/payment-gateways");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
