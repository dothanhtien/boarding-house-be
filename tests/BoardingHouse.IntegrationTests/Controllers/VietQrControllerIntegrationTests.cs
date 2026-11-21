using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Domain.VietQr;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class VietQrControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "vietqr-actor@test.com";
    private const string Password = "password123";

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
            Phone = "0900000093",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "VietQR Actor"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        await factory.GrantPlatformAdminRoleAsync(actor.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = ActorEmail, Password = Password });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
    }

    private async Task<HttpClient> RegisterAndLoginAsync(string fullName)
    {
        var email = $"user-{Guid.NewGuid():N}@test.com";
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = fullName
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return client;
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    [Fact]
    public async Task GetBanks_ReturnsEverySupportedBankInOrder()
    {
        var response = await _client.GetAsync("/api/vietqr/banks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var banks = await ReadDataAsync<List<VietQrBankResponse>>(response);
        Assert.Equal(VietQrBanks.All.Select(b => b.Bin), banks.Select(b => b.Bin));
        Assert.Contains(banks, b => b is { Bin: "970436", Code: "VCB", ShortName: "Vietcombank" });
    }

    [Fact]
    public async Task GetBanks_UsesCamelCaseFields()
    {
        var response = await _client.GetAsync("/api/vietqr/banks");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = document.RootElement.GetProperty("data")[0];
        Assert.Equal("970415", first.GetProperty("bin").GetString());
        Assert.Equal("ICB", first.GetProperty("code").GetString());
        Assert.Equal("VietinBank", first.GetProperty("shortName").GetString());
    }

    [Fact]
    public async Task GetBanks_UserWithoutAnyRole_ReturnsOk()
    {
        var client = await RegisterAndLoginAsync("No Role");

        var response = await client.GetAsync("/api/vietqr/banks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBanks_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/vietqr/banks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
