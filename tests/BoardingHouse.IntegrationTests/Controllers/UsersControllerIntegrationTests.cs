using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class UsersControllerIntegrationTests(PostgresApiFactory factory)
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
        var tokens = (await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())?.Data;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }

    private static CreateUserRequest ValidCreateRequest(string email = "user@test.com") => new()
    {
        Email = email,
        Phone = "0900000000",
        Password = "password123",
        PasswordConfirmation = "password123",
        FullName = "Test User"
    };

    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndBody()
    {
        var response = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        Assert.NotNull(body);
        Assert.Equal("user@test.com", body!.Email);
        Assert.True(body.IsActive);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest());

        var response = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidPayload_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "not-an-email",
            Password = "123",
            PasswordConfirmation = "456",
            FullName = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoOtherUsers_ReturnsOnlyAuthenticatedActor()
    {
        var response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.NotNull(body);
        Assert.Single(body!.Items);
        Assert.Equal(ActorEmail, body.Items[0].Email);
    }

    [Fact]
    public async Task GetAll_ExistingUsers_ReturnsAllExcludingDeleted()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("keep@test.com") with { Phone = "0911111111" });
        var kept = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var toDeleteResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("delete@test.com") with { Phone = "0922222222" });
        var toDelete = (await toDeleteResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        await _client.DeleteAsync($"/api/users/{toDelete!.Id}");

        var response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.NotNull(body);
        Assert.Contains(body!.Items, u => u.Id == kept!.Id);
        Assert.DoesNotContain(body.Items, u => u.Id == toDelete.Id);
    }

    [Fact]
    public async Task GetAll_MultiplePages_ReturnsCorrectPageAndMetadata()
    {
        for (var i = 0; i < 25; i++)
        {
            await _client.PostAsJsonAsync("/api/users", ValidCreateRequest($"user{i}@test.com") with { Phone = $"09{i:D8}" });
        }

        var page1Response = await _client.GetAsync("/api/users?page=1&pageSize=10");
        var page1 = (await page1Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.Equal(10, page1!.Items.Count);
        Assert.Equal(26, page1.TotalItems); // 25 + actor
        Assert.Equal(3, page1.TotalPages);

        var page3Response = await _client.GetAsync("/api/users?page=3&pageSize=10");
        var page3 = (await page3Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.Equal(6, page3!.Items.Count);

        var page99Response = await _client.GetAsync("/api/users?page=99&pageSize=10");
        var page99 = (await page99Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.Empty(page99!.Items);
    }

    [Fact]
    public async Task GetAll_SearchByEmailOrFullName_ReturnsMatchingItemsOnly()
    {
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("coffee.lover@test.com") with { Phone = "0911111112" });
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("tea.lover@test.com") with { Phone = "0911111113" });

        var response = await _client.GetAsync("/api/users?search=coffee");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;

        Assert.Single(result!.Items);
        Assert.Equal("coffee.lover@test.com", result.Items[0].Email);
    }

    [Fact]
    public async Task GetAll_FilterByIsActive_ReturnsMatchingItemsOnly()
    {
        var inactiveResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("inactive@test.com") with { Phone = "0911111114" });
        var inactive = (await inactiveResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        await _client.PutAsJsonAsync($"/api/users/{inactive!.Id}", new UpdateUserRequest { Phone = inactive.Phone, FullName = inactive.FullName, IsActive = false });

        var filteredResponse = await _client.GetAsync("/api/users?isActive=false");
        var filtered = (await filteredResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.All(filtered!.Items, u => Assert.False(u.IsActive));
        Assert.Contains(filtered.Items, u => u.Id == inactive.Id);

        var allResponse = await _client.GetAsync("/api/users");
        var all = (await allResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.Contains(all!.Items, u => u.Id == inactive.Id);
        Assert.True(all.Items.Count > filtered.Items.Count);
    }

    [Fact]
    public async Task GetAll_SortByEmailDescending_ReturnsInDescendingOrder()
    {
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("alpha@test.com") with { Phone = "0911111115" });
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("zeta@test.com") with { Phone = "0911111116" });

        var response = await _client.GetAsync("/api/users?sortBy=email&sortDescending=true");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;

        var emails = result!.Items.Select(u => u.Email).ToList();
        Assert.Equal(emails.OrderByDescending(e => e), emails);
    }

    [Fact]
    public async Task GetAll_SortByUnknownField_FallsBackToDefaultWithoutError()
    {
        var response = await _client.GetAsync("/api/users?sortBy=unknown-field");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_PageBeyondIntOverflowThreshold_ReturnsEmptyWithoutError()
    {
        // (page - 1) * pageSize must not overflow Int32 and wrap into a negative SQL OFFSET.
        var response = await _client.GetAsync("/api/users?page=21474838&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task GetAll_SearchContainingLikeWildcards_MatchesLiterally()
    {
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("john_doe@test.com") with { Phone = "0911111117" });
        await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("johnxdoe@test.com") with { Phone = "0911111118" });

        var response = await _client.GetAsync("/api/users?search=john_doe");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;

        Assert.Single(result!.Items);
        Assert.Equal("john_doe@test.com", result.Items[0].Email);
    }

    [Fact]
    public async Task GetAll_TiedSortValues_PaginatesWithoutDuplicatesAcrossPages()
    {
        for (var i = 0; i < 15; i++)
        {
            await _client.PostAsJsonAsync("/api/users", ValidCreateRequest($"tied{i}@test.com") with { Phone = $"0922{i:D6}" });
        }

        var page1Response = await _client.GetAsync("/api/users?page=1&pageSize=10&sortBy=isActive");
        var page1 = (await page1Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;

        var page2Response = await _client.GetAsync("/api/users?page=2&pageSize=10&sortBy=isActive");
        var page2 = (await page2Response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UserResponse>>>())?.Data;

        var page1Ids = page1!.Items.Select(u => u.Id);
        var page2Ids = page2!.Items.Select(u => u.Id);

        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}", new UpdateUserRequest
        {
            Phone = "0900000000",
            FullName = "Test User",
            IsActive = true
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateUpdateDelete_BehavesCorrectly()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest());
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var updateResponse = await _client.PutAsJsonAsync($"/api/users/{created!.Id}", new UpdateUserRequest
        {
            Phone = "0911111111",
            FullName = "Updated Name",
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        Assert.Equal("Updated Name", updated!.FullName);
        Assert.False(updated.IsActive);

        var deleteResponse = await _client.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await _client.GetAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithoutAuthentication_Returns401()
    {
        using var unauthenticatedClient = factory.CreateClient();

        var response = await unauthenticatedClient.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
