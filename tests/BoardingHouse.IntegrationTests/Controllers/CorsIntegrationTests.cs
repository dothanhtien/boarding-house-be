using System.Net.Http.Json;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class CorsIntegrationTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Request_FromAllowedOrigin_ReturnsAllowOriginHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@test.com", password = "wrong" })
        };

        request.Headers.Add("Origin", "https://allowed.example.com");

        var response = await _client.SendAsync(request);

        Assert.Equal("https://allowed.example.com", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task Request_FromDisallowedOrigin_DoesNotReturnAllowOriginHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@test.com", password = "wrong" })
        };
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
