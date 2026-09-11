using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class OrganizationsControllerIntegrationTests(PostgresApiFactory factory)
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

    private static CreateOrganizationRequest ValidCreateRequest() => new()
    {
        Name = "Test Organization"
    };

    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndBody()
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;
        Assert.NotNull(body);
        Assert.Equal("Test Organization", body!.Name);
        Assert.True(body.IsActive);
    }

    [Fact]
    public async Task Create_DuplicateTaxCode_Returns201ForBoth()
    {
        var firstResponse = await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());
        var secondResponse = await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/organizations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateUpdateDelete_BehavesCorrectly()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;

        var updateResponse = await _client.PutAsJsonAsync($"/api/organizations/{created!.Id}", new UpdateOrganizationRequest
        {
            Name = "Updated Organization",
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;

        Assert.Equal("Updated Organization", updated!.Name);
        Assert.False(updated.IsActive);

        var deleteResponse = await _client.DeleteAsync($"/api/organizations/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await _client.GetAsync($"/api/organizations/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithoutAuthentication_Returns401()
    {
        using var unauthenticatedClient = factory.CreateClient();

        var response = await unauthenticatedClient.GetAsync("/api/organizations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AuthenticatedWithoutOrganizationReadPermission_Returns403()
    {
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

        var response = await unprivilegedClient.GetAsync("/api/organizations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_MultiplePages_ReturnsCorrectPageAndMetadata()
    {
        for (var i = 0; i < 25; i++)
        {
            await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());
        }

        var page1Response = await _client.GetAsync("/api/organizations?page=1&pageSize=10");
        var page1 = (await page1Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;
        Assert.Equal(10, page1!.Items.Count);
        Assert.Equal(25, page1.TotalItems);
        Assert.Equal(3, page1.TotalPages);

        var page3Response = await _client.GetAsync("/api/organizations?page=3&pageSize=10");
        var page3 = (await page3Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;
        Assert.Equal(5, page3!.Items.Count);

        var page99Response = await _client.GetAsync("/api/organizations?page=99&pageSize=10");
        var page99 = (await page99Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;
        Assert.Empty(page99!.Items);
    }

    [Fact]
    public async Task GetAll_SearchByName_ReturnsMatchingItemsOnly()
    {
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "Coffee House" });
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "Tea Shop" });

        var response = await _client.GetAsync("/api/organizations?search=coffee");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;

        Assert.Single(result!.Items);
        Assert.Equal("Coffee House", result.Items[0].Name);
    }

    [Fact]
    public async Task GetAll_FilterByIsActive_ReturnsMatchingItemsOnly()
    {
        var activeResponse = await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());
        var active = (await activeResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;

        var inactiveResponse = await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());
        var inactive = (await inactiveResponse.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;
        await _client.PutAsJsonAsync($"/api/organizations/{inactive!.Id}", new UpdateOrganizationRequest { Name = inactive.Name, IsActive = false });

        var filteredResponse = await _client.GetAsync("/api/organizations?isActive=false");
        var filtered = (await filteredResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;
        Assert.All(filtered!.Items, o => Assert.False(o.IsActive));
        Assert.Contains(filtered.Items, o => o.Id == inactive.Id);
        Assert.DoesNotContain(filtered.Items, o => o.Id == active!.Id);

        var allResponse = await _client.GetAsync("/api/organizations");
        var all = (await allResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;
        Assert.Contains(all!.Items, o => o.Id == active!.Id);
        Assert.Contains(all.Items, o => o.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAll_SortByNameDescending_ReturnsInDescendingOrder()
    {
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "Alpha" });
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "Zeta" });

        var response = await _client.GetAsync("/api/organizations?sortBy=name&sortDescending=true");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;

        var names = result!.Items.Select(o => o.Name).ToList();
        Assert.Equal(names.OrderByDescending(n => n), names);
    }

    [Fact]
    public async Task GetAll_SortByUnknownField_FallsBackToDefaultWithoutError()
    {
        var response = await _client.GetAsync("/api/organizations?sortBy=unknown-field");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_PageBeyondIntOverflowThreshold_ReturnsEmptyWithoutError()
    {
        // (page - 1) * pageSize must not overflow Int32 and wrap into a negative SQL OFFSET.
        var response = await _client.GetAsync("/api/organizations?page=21474838&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task GetAll_SearchContainingLikeWildcards_MatchesLiterally()
    {
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "john_doe" });
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = "johnxdoe" });

        var response = await _client.GetAsync("/api/organizations?search=john_doe");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;

        Assert.Single(result!.Items);
        Assert.Equal("john_doe", result.Items[0].Name);
    }

    [Fact]
    public async Task GetAll_TiedSortValues_PaginatesWithoutDuplicatesAcrossPages()
    {
        for (var i = 0; i < 15; i++)
        {
            await _client.PostAsJsonAsync("/api/organizations", ValidCreateRequest());
        }

        var page1Response = await _client.GetAsync("/api/organizations?page=1&pageSize=10");
        var page1 = (await page1Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;

        var page2Response = await _client.GetAsync("/api/organizations?page=2&pageSize=10");
        var page2 = (await page2Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrganizationResponse>>>())?.Data;

        var page1Ids = page1!.Items.Select(o => o.Id);
        var page2Ids = page2!.Items.Select(o => o.Id);

        Assert.Empty(page1Ids.Intersect(page2Ids));
    }
}
