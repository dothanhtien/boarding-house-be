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

    private static CreateOrganizationRequest ValidCreateRequest(string taxCode = "0100100100") => new()
    {
        Name = "Test Organization",
        TaxCode = taxCode
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
        var tokens = (await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())?.Data;
        unprivilegedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await unprivilegedClient.GetAsync("/api/organizations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
