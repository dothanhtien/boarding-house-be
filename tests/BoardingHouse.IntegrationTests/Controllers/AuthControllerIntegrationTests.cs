using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
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

    private static string ExtractCookie(HttpResponseMessage response, string cookieName)
    {
        var setCookieHeader = response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith($"{cookieName}=", StringComparison.Ordinal));

        return setCookieHeader.Split(';')[0][$"{cookieName}=".Length..];
    }

    private static HttpRequestMessage WithCookies(HttpMethod method, string url, params (string Name, string Value)[] cookies)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
        return request;
    }

    private static HttpRequestMessage PostWithRefreshTokenCookie(string url, string refreshTokenCookieValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"refreshToken={refreshTokenCookieValue}");
        return request;
    }

    private static void AssertAuthCookiesCleared(HttpResponseMessage response)
    {
        foreach (var cookieName in new[] { "accessToken", "refreshToken" })
        {
            var setCookieHeader = response.Headers.GetValues("Set-Cookie")
                .Single(h => h.StartsWith($"{cookieName}=", StringComparison.Ordinal));
            Assert.Contains("01 Jan 1970", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<(Guid UserId, string AccessTokenCookieValue, string RefreshTokenCookieValue)> RegisterAndLoginAsync(
        string email = "user@test.com", string phone = "0900000000")
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest(email) with { Phone = phone });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = Password
        });

        var accessTokenCookieValue = ExtractCookie(loginResponse, "accessToken");
        var refreshTokenCookieValue = ExtractCookie(loginResponse, "refreshToken");

        return (user!.Id, accessTokenCookieValue, refreshTokenCookieValue);
    }

    private static HttpRequestMessage AuthorizedGet(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage AuthorizedPatch<T>(string url, string accessToken, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body)
        };
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

        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
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
    public async Task Login_ValidCredentials_Returns200WithUserAndSetsCookie()
    {
        await _client.PostAsJsonAsync("/api/auth/register", ValidRegisterRequest());

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "user@test.com",
            Password = Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        Assert.NotNull(body);
        Assert.Equal("user@test.com", body.Email);

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookieHeaders, h => h.StartsWith("accessToken=", StringComparison.Ordinal) && h.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, h => h.StartsWith("refreshToken=", StringComparison.Ordinal) && h.Contains("httponly", StringComparison.OrdinalIgnoreCase));
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
    public async Task Refresh_ValidToken_Returns204AndRotatesCookies()
    {
        var (_, accessTokenCookieValue, refreshTokenCookieValue) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/refresh-token", refreshTokenCookieValue));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotEqual(accessTokenCookieValue, ExtractCookie(response, "accessToken"));
        Assert.NotEqual(refreshTokenCookieValue, ExtractCookie(response, "refreshToken"));
    }

    [Fact]
    public async Task Refresh_UsedToken_Returns401AndRevokesAllActiveTokens()
    {
        var (_, _, refreshTokenCookieValue) = await RegisterAndLoginAsync();

        var firstRefreshResponse = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/refresh-token", refreshTokenCookieValue));
        var rotatedCookieValue = ExtractCookie(firstRefreshResponse, "refreshToken");

        var reuseResponse = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/refresh-token", refreshTokenCookieValue));

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
        AssertAuthCookiesCleared(reuseResponse);

        var rotatedRefreshResponse = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/refresh-token", rotatedCookieValue));

        Assert.Equal(HttpStatusCode.Unauthorized, rotatedRefreshResponse.StatusCode);
        AssertAuthCookiesCleared(rotatedRefreshResponse);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401AndClearsCookie()
    {
        var response = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/refresh-token", "not-a-real-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertAuthCookiesCleared(response);
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        var response = await _client.PostAsync("/api/auth/refresh-token", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ValidToken_Returns204AndInvalidatesToken()
    {
        var (_, _, refreshTokenCookieValue) = await RegisterAndLoginAsync();

        var logoutResponse = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/logout", refreshTokenCookieValue));

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var expiredSetCookieHeader = logoutResponse.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("refreshToken=", StringComparison.Ordinal));
        Assert.Contains("01 Jan 1970", expiredSetCookieHeader, StringComparison.OrdinalIgnoreCase);

        var refreshResponse = await _client.SendAsync(PostWithRefreshTokenCookie("/api/auth/refresh-token", refreshTokenCookieValue));

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_NoCookie_Returns204()
    {
        var response = await _client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Me_ValidToken_Returns200WithCurrentUser()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        Assert.Equal(userId, body!.Id);
    }

    [Fact]
    public async Task Me_UserWithPlatformRoleAndOrganizationMembership_ReturnsEnrichedResponse()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();
        await factory.GrantPlatformAdminRoleAsync(userId);

        Guid organizationId;
        string organizationRoleSlug;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organizationRole = await context.Roles.SingleAsync(r => r.Slug == "organization_admin");
            var organization = new Organization { Name = "Member Org", CreatedBy = SentinelActors.System };
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();

            context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organization.Id,
                UserId = userId,
                RoleId = organizationRole.Id,
                CreatedBy = SentinelActors.System
            });
            await context.SaveChangesAsync();

            organizationId = organization.Id;
            organizationRoleSlug = organizationRole.Slug;
        }

        var response = await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        Assert.Equal("platform_admin", body!.PlatformRole?.Slug);
        var organizationEntry = Assert.Single(body.Organizations);
        Assert.Equal(organizationId, organizationEntry.OrganizationId);
        Assert.Equal("Member Org", organizationEntry.OrganizationName);
        Assert.Equal(organizationRoleSlug, organizationEntry.RoleSlug);
    }

    [Fact]
    public async Task Me_NoToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_CookieOnly_Returns200WithCurrentUser()
    {
        var (userId, accessTokenCookieValue, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(WithCookies(HttpMethod.Get, "/api/auth/me", ("accessToken", accessTokenCookieValue)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        Assert.Equal(userId, body!.Id);
    }

    [Fact]
    public async Task Me_BearerAndCookiePresent_BearerTakesPrecedence()
    {
        var (userId, accessTokenCookieValue, _) = await RegisterAndLoginAsync();

        var request = WithCookies(HttpMethod.Get, "/api/auth/me", ("accessToken", "not-a-real-token"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessTokenCookieValue);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;
        Assert.Equal(userId, body!.Id);
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

        await factory.GrantPlatformAdminRoleAsync(userId);

        var updateResponse = await _client.SendAsync(AuthorizedPatch($"/api/users/{userId}", accessToken, new UpdateUserRequest
        {
            Phone = "0900000000",
            FullName = "Test User",
            IsActive = false
        }));
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

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] PdfBytes = "%PDF-1.7\n"u8.ToArray();

    private static MultipartFormDataContent AvatarForm(byte[] content, string contentType = "image/png", string fileName = "me.png")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string url, string accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<UserResponse> UpdateMyAvatarAsync(string accessToken, string fileName = "me.png")
    {
        var response = await _client.SendAsync(
            AuthorizedRequest(HttpMethod.Put, "/api/auth/me/avatar", accessToken, AvatarForm(PngBytes, fileName: fileName)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;
    }

    private async Task<List<MediaAsset>> GetAvatarRowsAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.MediaAssets
            .IgnoreQueryFilters()
            .Where(m => m.EntityType == MediaAssetEntityType.UserAvatar && m.EntityId == userId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    [Fact]
    public async Task UpdateMyAvatar_UserWithoutAnyRole_StoresLinkedPlatformAssetAndReturnsAvatarUrl()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        var updated = await UpdateMyAvatarAsync(accessToken);

        Assert.Equal(userId, updated.Id);
        var row = Assert.Single(await GetAvatarRowsAsync(userId));
        Assert.Null(row.OrganizationId);
        Assert.Null(row.DeletedAt);
        Assert.Equal("image/png", row.MimeType);
        Assert.StartsWith($"test/users/{userId}/", row.StorageKey);
        Assert.True(factory.Storage.Files.ContainsKey(row.StorageKey));
        Assert.Equal($"https://storage.test/{row.StorageKey}", updated.AvatarUrl);

        var me = (await (await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken)))
            .Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;
        Assert.Equal(updated.AvatarUrl, me.AvatarUrl);
    }

    [Fact]
    public async Task UpdateMyAvatar_UserWithPlatformRoleAndOrganizationMembership_ReturnsSameEnrichedResponseAsMe()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();
        await factory.GrantPlatformAdminRoleAsync(userId);

        Guid organizationId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organizationRole = await context.Roles.SingleAsync(r => r.Slug == "organization_admin");
            var organization = new Organization { Name = "Member Org", CreatedBy = SentinelActors.System };
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();

            context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organization.Id,
                UserId = userId,
                RoleId = organizationRole.Id,
                CreatedBy = SentinelActors.System
            });
            await context.SaveChangesAsync();

            organizationId = organization.Id;
        }

        var updated = await UpdateMyAvatarAsync(accessToken);

        var me = (await (await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken)))
            .Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;
        Assert.Equal("platform_admin", updated.PlatformRole?.Slug);
        Assert.Equal(me.PlatformRole?.Id, updated.PlatformRole?.Id);
        var organizationEntry = Assert.Single(updated.Organizations);
        Assert.Equal(organizationId, organizationEntry.OrganizationId);
        Assert.Equal(me.Organizations, updated.Organizations);
        Assert.Equal(me.AvatarUrl, updated.AvatarUrl);
    }

    [Fact]
    public async Task UpdateMyAvatar_Replaced_SoftDeletesPreviousAssetAndDeletesItsFile()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();
        await UpdateMyAvatarAsync(accessToken);

        var updated = await UpdateMyAvatarAsync(accessToken, "new.png");

        var rows = await GetAvatarRowsAsync(userId);
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].DeletedAt);
        Assert.Null(rows[1].DeletedAt);
        Assert.Equal("new.png", rows[1].FileName);
        Assert.False(factory.Storage.Files.ContainsKey(rows[0].StorageKey));
        Assert.True(factory.Storage.Files.ContainsKey(rows[1].StorageKey));
        Assert.Equal($"https://storage.test/{rows[1].StorageKey}", updated.AvatarUrl);
    }

    [Fact]
    public async Task UpdateMyAvatar_OnlyTouchesCallersOwnAvatar()
    {
        var (otherUserId, otherAccessToken, _) = await RegisterAndLoginAsync("other@test.com", "0900000001");
        await UpdateMyAvatarAsync(otherAccessToken);
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        await UpdateMyAvatarAsync(accessToken);
        var deleteResponse = await _client.SendAsync(AuthorizedRequest(HttpMethod.Delete, "/api/auth/me/avatar", accessToken));

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.NotNull(Assert.Single(await GetAvatarRowsAsync(userId)).DeletedAt);
        Assert.Null(Assert.Single(await GetAvatarRowsAsync(otherUserId)).DeletedAt);
    }

    [Fact]
    public async Task UpdateMyAvatar_NoFile_Returns400()
    {
        var (_, accessToken, _) = await RegisterAndLoginAsync();

        var form = new MultipartFormDataContent { { new StringContent("x"), "unknownField" } };
        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Put, "/api/auth/me/avatar", accessToken, form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyAvatar_NotAnImage_Returns400AndStoresNothing()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthorizedRequest(
            HttpMethod.Put, "/api/auth/me/avatar", accessToken, AvatarForm(PdfBytes, "application/pdf", "cv.pdf")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await GetAvatarRowsAsync(userId));
        Assert.Empty(factory.Storage.Files);
    }

    [Fact]
    public async Task UpdateMyAvatar_ContentNotMatchingDeclaredImageType_Returns400AndStoresNothing()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthorizedRequest(
            HttpMethod.Put, "/api/auth/me/avatar", accessToken, AvatarForm("not an image"u8.ToArray())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await GetAvatarRowsAsync(userId));
        Assert.Empty(factory.Storage.Files);
    }

    [Fact]
    public async Task UpdateMyAvatar_JsonBody_Returns415()
    {
        var (_, accessToken, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthorizedRequest(
            HttpMethod.Put, "/api/auth/me/avatar", accessToken, JsonContent.Create(new { file = "x" })));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyAvatar_WithoutAuthentication_Returns401()
    {
        var response = await _client.PutAsync("/api/auth/me/avatar", AvatarForm(PngBytes));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Storage.Files);
    }

    [Fact]
    public async Task DeleteMyAvatar_RemovesAvatarAndItsFile()
    {
        var (userId, accessToken, _) = await RegisterAndLoginAsync();
        await UpdateMyAvatarAsync(accessToken);

        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Delete, "/api/auth/me/avatar", accessToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = Assert.Single(await GetAvatarRowsAsync(userId));
        Assert.NotNull(row.DeletedAt);
        Assert.Empty(factory.Storage.Files);
        var me = (await (await _client.SendAsync(AuthorizedGet("/api/auth/me", accessToken)))
            .Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;
        Assert.Null(me.AvatarUrl);
    }

    [Fact]
    public async Task DeleteMyAvatar_WithoutAvatar_Returns204()
    {
        var (_, accessToken, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Delete, "/api/auth/me/avatar", accessToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMyAvatar_WithoutAuthentication_Returns401()
    {
        var response = await _client.DeleteAsync("/api/auth/me/avatar");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
