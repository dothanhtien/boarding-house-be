using System.Net;
using System.Net.Http.Json;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class AuthControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private const string Password = "password123";

    private static RegisterRequest ValidRegisterRequest(string email = "user@test.com") => new()
    {
        Email = email,
        Phone = "0900000000",
        Password = Password,
        PasswordConfirmation = Password,
        FullName = "Test User"
    };

    private async Task<AuthResponse> RegisterAndLoginAsync(string email = "user@test.com")
    {
        await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest(email));

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = Password
        });

        return (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task DeactivateUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await context.Users.SingleAsync(u => u.Email == email);
        user.IsActive = false;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Register_ValidRequest_Returns200WithUser()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.Equal("user@test.com", body.Email);
        Assert.True(body.IsActive);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());

        var response = await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidPayload_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "not-an-email",
            Password = "123",
            PasswordConfirmation = "456",
            FullName = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "user@test.com",
            Password = Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "user@test.com",
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InactiveUser_Returns401()
    {
        await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());
        await DeactivateUserAsync("user@test.com");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "user@test.com",
            Password = Password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "unknown@test.com",
            Password = Password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithRotatedTokens()
    {
        var tokens = await RegisterAndLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = tokens.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(tokens.RefreshToken, body.RefreshToken);
        Assert.NotEqual(tokens.AccessToken, body.AccessToken);
    }

    [Fact]
    public async Task Refresh_UsedToken_Returns401AndRevokesAllActiveTokens()
    {
        var tokens = await RegisterAndLoginAsync();

        var firstRefreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = tokens.RefreshToken
        });
        var rotatedTokens = await firstRefreshResponse.Content.ReadFromJsonAsync<AuthResponse>();

        // Reusing the already-rotated (revoked) refresh token should fail and revoke the whole chain.
        var reuseResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = tokens.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        var rotatedRefreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = rotatedTokens!.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, rotatedRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = "not-a-real-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ValidToken_Returns204AndInvalidatesToken()
    {
        var tokens = await RegisterAndLoginAsync();

        var logoutResponse = await _client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest
        {
            RefreshToken = tokens.RefreshToken
        });

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = tokens.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_UnknownToken_Returns204()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest
        {
            RefreshToken = "not-a-real-token"
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
