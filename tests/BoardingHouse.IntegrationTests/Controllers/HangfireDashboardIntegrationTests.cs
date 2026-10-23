using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Infrastructure.BackgroundJobs;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class HangfireDashboardIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private const string Password = "password123";

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> CreateAuthenticatedClientAsync(bool platformAdmin)
    {
        var client = factory.CreateClient();
        var email = $"dashboard-{Guid.NewGuid():N}@test.com";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Dashboard User"
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        if (platformAdmin)
        {
            await factory.GrantPlatformAdminRoleAsync(user.Id);
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        var accessToken = loginResponse.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal))
            .Split(';')[0]["accessToken=".Length..];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    [Fact]
    public async Task Dashboard_Anonymous_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_UserWithoutPermission_Returns403()
    {
        var client = await CreateAuthenticatedClientAsync(platformAdmin: false);

        var response = await client.GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_PlatformAdmin_Returns200()
    {
        var client = await CreateAuthenticatedClientAsync(platformAdmin: true);

        var response = await client.GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void BackgroundJobServer_DisabledInTests_IsNotRegistered()
    {
        var hostedServices = factory.Services.GetServices<IHostedService>().ToList();

        Assert.DoesNotContain(hostedServices, s => s is RecurringJobRegistry);
        Assert.DoesNotContain(hostedServices, s => s.GetType().Name == "BackgroundJobServerHostedService");
    }
}
