using System.Net;
using System.Net.Http.Json;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class UsersControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

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
    public async Task FullLifecycle_CreateUpdateDelete_BehavesCorrectly()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/users", ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();

        var updateResponse = await _client.PutAsJsonAsync($"/api/users/{created!.Id}", new UpdateUserRequest
        {
            Phone = "0911111111",
            FullName = "Updated Name",
            IsActive = false
        });
        var updated = await updateResponse.Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Updated Name", updated!.FullName);
        Assert.False(updated.IsActive);

        var deleteResponse = await _client.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await _client.GetAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }
}
