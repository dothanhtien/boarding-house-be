using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class OrganizationMembersControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string ActorEmail = "actor@test.com";
    private const string ActorPassword = "password123";

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

        await factory.GrantPlatformAdminRoleAsync(actor!.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ActorEmail,
            Password = ActorPassword
        });

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
    }

    private async Task<Guid> CreateOrganizationAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "Test Organization" });
        var organization = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;

        return organization!.Id;
    }

    private async Task<Guid> RegisterUserAsync(string email, string phone)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = phone,
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Member User"
        });
        var user = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        return user!.Id;
    }

    private async Task<Guid> GetRoleIdBySlugAsync(string slug)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await context.Roles.SingleAsync(r => r.Slug == slug);

        return role.Id;
    }

    [Fact]
    public async Task AddMember_OrganizationScopedRole_Returns201()
    {
        var organizationId = await CreateOrganizationAsync();
        var userId = await RegisterUserAsync("member1@test.com", "0900000001");
        var roleId = await GetRoleIdBySlugAsync("organization_staff");

        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members",
            new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationMemberResponse>>())?.Data;
        Assert.NotNull(body);
        Assert.Equal(userId, body!.UserId);
        Assert.Equal(roleId, body.RoleId);
        Assert.Equal("organization_staff", body.RoleSlug);
    }

    [Fact]
    public async Task AddMember_PlatformScopedRole_Returns400()
    {
        var organizationId = await CreateOrganizationAsync();
        var userId = await RegisterUserAsync("member2@test.com", "0900000002");
        var roleId = await GetRoleIdBySlugAsync("platform_admin");

        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members",
            new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_AlreadyAMember_Returns409()
    {
        var organizationId = await CreateOrganizationAsync();
        var userId = await RegisterUserAsync("member3@test.com", "0900000003");
        var roleId = await GetRoleIdBySlugAsync("organization_staff");
        var request = new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId };

        var firstResponse = await _client.PostAsJsonAsync($"/api/organizations/{organizationId}/members", request);
        var secondResponse = await _client.PostAsJsonAsync($"/api/organizations/{organizationId}/members", request);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_ToPlatformScopedRole_Returns400()
    {
        var organizationId = await CreateOrganizationAsync();
        var userId = await RegisterUserAsync("member4@test.com", "0900000004");
        var staffRoleId = await GetRoleIdBySlugAsync("organization_staff");
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members",
            new AddOrganizationMemberRequest { UserId = userId, RoleId = staffRoleId });
        var member = (await addResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationMemberResponse>>())?.Data;

        var platformRoleId = await GetRoleIdBySlugAsync("platform_staff");
        var response = await _client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{member!.Id}",
            new UpdateOrganizationMemberRequest { RoleId = platformRoleId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_OrganizationScopedRole_Returns200WithUpdatedRole()
    {
        var organizationId = await CreateOrganizationAsync();
        var userId = await RegisterUserAsync("member5@test.com", "0900000005");
        var staffRoleId = await GetRoleIdBySlugAsync("organization_staff");
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members",
            new AddOrganizationMemberRequest { UserId = userId, RoleId = staffRoleId });
        var member = (await addResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationMemberResponse>>())?.Data;

        var adminRoleId = await GetRoleIdBySlugAsync("organization_admin");
        var response = await _client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{member!.Id}",
            new UpdateOrganizationMemberRequest { RoleId = adminRoleId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationMemberResponse>>())?.Data;
        Assert.Equal("organization_admin", updated!.RoleSlug);
    }

    [Fact]
    public async Task RemoveMember_SoftDeletesAndAllowsReAdding()
    {
        var organizationId = await CreateOrganizationAsync();
        var userId = await RegisterUserAsync("member6@test.com", "0900000006");
        var roleId = await GetRoleIdBySlugAsync("organization_staff");
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members",
            new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId });
        var member = (await addResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationMemberResponse>>())?.Data;

        var deleteResponse = await _client.DeleteAsync($"/api/organizations/{organizationId}/members/{member!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _client.GetAsync($"/api/organizations/{organizationId}/members");
        var list = (await listResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationMemberResponse>>>())?.Data;
        Assert.DoesNotContain(list!.Items, m => m.Id == member.Id);

        var reAddResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/members",
            new AddOrganizationMemberRequest { UserId = userId, RoleId = roleId });
        Assert.Equal(HttpStatusCode.Created, reAddResponse.StatusCode);
    }

    [Fact]
    public async Task GetMembers_AuthenticatedWithoutOrganizationMemberReadPermission_Returns403()
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

        var response = await unprivilegedClient.GetAsync($"/api/organizations/{organizationId}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMembers_PageSizeSmallerThanTotal_ReturnsCorrectPageAndTotalItems()
    {
        var organizationId = await CreateOrganizationAsync();
        var roleId = await GetRoleIdBySlugAsync("organization_staff");

        var firstUserId = await RegisterUserAsync("member7@test.com", "0900000007");
        var secondUserId = await RegisterUserAsync("member8@test.com", "0900000008");
        await _client.PostAsJsonAsync($"/api/organizations/{organizationId}/members", new AddOrganizationMemberRequest { UserId = firstUserId, RoleId = roleId });
        await _client.PostAsJsonAsync($"/api/organizations/{organizationId}/members", new AddOrganizationMemberRequest { UserId = secondUserId, RoleId = roleId });

        var response = await _client.GetAsync($"/api/organizations/{organizationId}/members?page=1&pageSize=1");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationMemberResponse>>>())?.Data;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(result!.Items);
        Assert.Equal(2, result.TotalItems);
    }
}
