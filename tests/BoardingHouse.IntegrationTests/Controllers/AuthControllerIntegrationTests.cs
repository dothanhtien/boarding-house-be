using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> RegisterAndLoginAsync(string email = "user@test.com")
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest(email));
        var user = await registerResponse.Content.ReadFromJsonAsync<UserResponse>();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = Password
        });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        return (user!.Id, tokens!.AccessToken, tokens.RefreshToken);
    }

    private static HttpRequestMessage AuthorizedGet(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string GenerateAccessTokenWithoutSubClaim()
    {
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestJwtOptions.Issuer,
            audience: TestJwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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
        var (_, accessToken, refreshToken) = await RegisterAndLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(refreshToken, body.RefreshToken);
        Assert.NotEqual(accessToken, body.AccessToken);
    }

    [Fact]
    public async Task Refresh_UsedToken_Returns401AndRevokesAllActiveTokens()
    {
        var (_, _, refreshToken) = await RegisterAndLoginAsync();

        var firstRefreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        });
        var rotatedTokens = await firstRefreshResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var reuseResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = refreshToken
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
        var (_, _, refreshToken) = await RegisterAndLoginAsync();

        var logoutResponse = await _client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        });

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = refreshToken
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

    [Fact]
    public async Task Me_ValidToken_Returns200WithCurrentUser()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(userId, body!.Id);
    }

    [Fact]
    public async Task Me_NoToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_TokenWithoutSubClaim_Returns401()
    {
        var token = GenerateAccessTokenWithoutSubClaim();

        var response = await _client.SendAsync(AuthorizedGet("/api/auth/me", token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AfterDeactivateViaApi_Returns401Immediately()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));

        var updateResponse = await _client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserRequest
        {
            Phone = "0900000000",
            FullName = "Test User",
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var response = await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AfterDirectDbChange_StaysValidUntilCacheExpires_ThenReturns401()
    {
        var (_, accessToken, _) = await RegisterAndLoginAsync();

        await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));

        await DeactivateUserAsync("user@test.com");

        var stillCachedResponse = await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));
        Assert.Equal(HttpStatusCode.OK, stillCachedResponse.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage afterExpiryResponse;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            afterExpiryResponse = await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));
        } while (afterExpiryResponse.StatusCode != HttpStatusCode.Unauthorized && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiryResponse.StatusCode);
    }
}
