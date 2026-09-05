using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = ActorEmail,
            Phone = "0900000099",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Actor User"
        });

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ActorEmail,
            Password = ActorPassword
        });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

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

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
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
        var body = await response.Content.ReadFromJsonAsync<List<UserResponse>>();
        Assert.NotNull(body);
        Assert.Single(body!);
        Assert.Equal(ActorEmail, body![0].Email);
    }

    [Fact]
    public async Task GetAll_ExistingUsers_ReturnsAllExcludingDeleted()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("keep@test.com") with { Phone = "0911111111" });
        var kept = await createResponse.Content.ReadFromJsonAsync<UserResponse>();

        var toDeleteResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest("delete@test.com") with { Phone = "0922222222" });
        var toDelete = await toDeleteResponse.Content.ReadFromJsonAsync<UserResponse>();
        await _client.DeleteAsync($"/api/users/{toDelete!.Id}");

        var response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<UserResponse>>();
        Assert.NotNull(body);
        Assert.Contains(body!, u => u.Id == kept!.Id);
        Assert.DoesNotContain(body!, u => u.Id == toDelete.Id);
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
        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();

        var updateResponse = await _client.PutAsJsonAsync($"/api/users/{created!.Id}", new UpdateUserRequest
        {
            Phone = "0911111111",
            FullName = "Updated Name",
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<UserResponse>();

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
