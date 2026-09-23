using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class PropertiesControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string ActorEmail = "property-actor@test.com";
    private const string ActorPassword = "password123";

    private Guid _actorId;
    private Guid _organizationId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Property Org");
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
            Phone = "0900000097",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Property Actor"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        await factory.GrantPlatformAdminRoleAsync(actor!.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ActorEmail,
            Password = ActorPassword
        });

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        _actorId = actor.Id;
    }

    private async Task<Guid> CreateOrganizationAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = name, OwnerId = _actorId });
        var organization = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;
        return organization!.Id;
    }

    private CreatePropertyRequest ValidCreateRequest() => new()
    {
        OrganizationId = _organizationId,
        Name = "Test Property"
    };

    private async Task<(HttpClient Client, Guid OrganizationId)> CreateOrganizationScopedMemberAsync(string roleSlug)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RbacSeeder.SeedAsync(context);

        var organization = new Organization { Name = $"Member Org {Guid.NewGuid()}", CreatedBy = SentinelActors.System };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var memberClient = factory.CreateClient();
        var email = $"member-{Guid.NewGuid():N}@test.com";
        var registerResponse = await memberClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Org Member"
        });
        var member = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var role = await context.Roles.SingleAsync(r => r.Slug == roleSlug);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = member!.Id,
            RoleId = role.Id,
            CreatedBy = SentinelActors.System
        });
        await context.SaveChangesAsync();

        var loginResponse = await memberClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = ActorPassword
        });
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        return (memberClient, organization.Id);
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201AndGetByIdSeesSameData()
    {
        var response = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        Assert.NotNull(created);
        Assert.Equal("Test Property", created!.Name);
        Assert.Equal(_organizationId, created.OrganizationId);
        Assert.True(created.IsActive);

        var getResponse = await _client.GetAsync($"/api/properties/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = (await getResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("Test Property", fetched.Name);
    }

    [Fact]
    public async Task Create_OrganizationIdNotFound_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { OrganizationId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameOrganization_ReturnsConflict()
    {
        var firstResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameInDifferentOrganization_Returns201()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Other Org");

        var firstResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync(
            "/api/properties", ValidCreateRequest() with { OrganizationId = otherOrganizationId });

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateUpdateDelete_BehavesCorrectly()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        var updateResponse = await _client.PatchAsJsonAsync($"/api/properties/{created!.Id}", new UpdatePropertyRequest
        {
            Name = "Updated Property",
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        Assert.Equal("Updated Property", updated!.Name);
        Assert.False(updated.IsActive);

        var deleteResponse = await _client.DeleteAsync($"/api/properties/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await _client.GetAsync($"/api/properties/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Endpoints_UserWithoutPlatformPermissionOrMembership_Return403()
    {
        using var unprivilegedClient = factory.CreateClient();
        var registerResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "no-permission-property@test.com",
            Phone = "0900000096",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "No Permission User"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "no-permission-property@test.com",
            Password = ActorPassword
        });
        unprivilegedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        var createResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.GetAsync("/api/properties")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.GetAsync($"/api/properties/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.PostAsJsonAsync("/api/properties", ValidCreateRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.PatchAsJsonAsync(
            $"/api/properties/{created.Id}", new UpdatePropertyRequest { Name = "X", IsActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.DeleteAsync($"/api/properties/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetAll_MultiplePages_ReturnsCorrectPageAndMetadata()
    {
        for (var i = 0; i < 25; i++)
        {
            await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = $"Property {i}" });
        }

        var page1Response = await _client.GetAsync("/api/properties?page=1&pageSize=10");
        var page1 = (await page1Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;
        Assert.Equal(10, page1!.Items.Count);
        Assert.Equal(25, page1.TotalItems);
    }

    [Fact]
    public async Task GetAll_SearchByName_ReturnsMatchingItemsOnly()
    {
        await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Coffee House" });
        await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Tea Shop" });

        var response = await _client.GetAsync("/api/properties?search=coffee");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;

        Assert.Single(result!.Items);
        Assert.Equal("Coffee House", result.Items[0].Name);
    }

    [Fact]
    public async Task GetAll_FilterByIsActive_ReturnsMatchingItemsOnly()
    {
        var activeResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Active" });
        var active = (await activeResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        var inactiveResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Inactive" });
        var inactive = (await inactiveResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        await _client.PatchAsJsonAsync($"/api/properties/{inactive!.Id}", new UpdatePropertyRequest { Name = inactive.Name, IsActive = false });

        var filteredResponse = await _client.GetAsync("/api/properties?isActive=false");
        var filtered = (await filteredResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;
        Assert.All(filtered!.Items, p => Assert.False(p.IsActive));
        Assert.Contains(filtered.Items, p => p.Id == inactive.Id);
        Assert.DoesNotContain(filtered.Items, p => p.Id == active!.Id);
    }

    [Fact]
    public async Task GetAll_FilterByOrganizationId_ReturnsOnlyThatOrganizationsProperties()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Filter Org");

        var ownResponse = await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest());
        var own = (await ownResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        var otherResponse = await _client.PostAsJsonAsync(
            "/api/properties", ValidCreateRequest() with { OrganizationId = otherOrganizationId, Name = "Other Property" });
        var other = (await otherResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        var response = await _client.GetAsync($"/api/properties?organizationId={otherOrganizationId}");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;

        Assert.Contains(result!.Items, p => p.Id == other!.Id);
        Assert.DoesNotContain(result.Items, p => p.Id == own!.Id);
    }

    [Fact]
    public async Task GetAll_SortByNameDescending_ReturnsInDescendingOrder()
    {
        await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Alpha" });
        await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Zeta" });

        var response = await _client.GetAsync("/api/properties?sortBy=name&sortOrder=desc");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;

        var names = result!.Items.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderByDescending(n => n), names);
    }

    [Fact]
    public async Task GetAll_SortByUnknownField_FallsBackToDefaultWithoutError()
    {
        var response = await _client.GetAsync("/api/properties?sortBy=unknown-field");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OrganizationScope_MemberOfOrganizationA_CanOnlyAccessOwnOrganizationsProperties()
    {
        var (memberClient, organizationAId) = await CreateOrganizationScopedMemberAsync("organization_admin");
        var organizationBId = await CreateOrganizationAsync("Org B");

        var createOwnResponse = await memberClient.PostAsJsonAsync(
            "/api/properties", new CreatePropertyRequest { OrganizationId = organizationAId, Name = "Own Property" });
        Assert.Equal(HttpStatusCode.Created, createOwnResponse.StatusCode);
        var own = (await createOwnResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        var createOtherResponse = await memberClient.PostAsJsonAsync(
            "/api/properties", new CreatePropertyRequest { OrganizationId = organizationBId, Name = "Other Property" });
        Assert.Equal(HttpStatusCode.NotFound, createOtherResponse.StatusCode);

        var otherOrgPropertyResponse = await _client.PostAsJsonAsync(
            "/api/properties", ValidCreateRequest() with { OrganizationId = organizationBId, Name = "Other Property" });
        var otherOrgProperty = (await otherOrgPropertyResponse.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;

        var listResponse = await memberClient.GetAsync("/api/properties");
        var list = (await listResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;
        Assert.Contains(list!.Items, p => p.Id == own!.Id);
        Assert.DoesNotContain(list.Items, p => p.Id == otherOrgProperty!.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/properties/{otherOrgProperty!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync(
            $"/api/properties/{otherOrgProperty.Id}", new UpdatePropertyRequest { Name = "X", IsActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/properties/{otherOrgProperty.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_NoOrganizationIdInBodyButHeaderPresent_UsesHeaderOrganization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new CreatePropertyRequest { Name = "Header Property" })
        };
        request.Headers.Add("X-Organization-Id", _organizationId.ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        Assert.Equal(_organizationId, created!.OrganizationId);
    }

    [Fact]
    public async Task Create_NoOrganizationIdInBodyAndNoHeader_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest { Name = "No Org" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_BodyOrganizationIdDiffersFromHeader_BodyTakesPrecedence()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Header Precedence Org");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new CreatePropertyRequest { OrganizationId = _organizationId, Name = "Precedence Property" })
        };
        request.Headers.Add("X-Organization-Id", otherOrganizationId.ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        Assert.Equal(_organizationId, created!.OrganizationId);
    }

    [Fact]
    public async Task GetAll_HeaderOrganizationIdWithoutQueryFilter_ReturnsOnlyThatOrganizationsProperties()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Header List Org");
        await _client.PostAsJsonAsync("/api/properties", ValidCreateRequest() with { Name = "Own Header Property" });
        await _client.PostAsJsonAsync(
            "/api/properties", ValidCreateRequest() with { OrganizationId = otherOrganizationId, Name = "Other Header Property" });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/properties");
        request.Headers.Add("X-Organization-Id", _organizationId.ToString());

        var response = await _client.SendAsync(request);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PropertyResponse>>>())?.Data;

        Assert.All(result!.Items, p => Assert.Equal(_organizationId, p.OrganizationId));
    }
}
