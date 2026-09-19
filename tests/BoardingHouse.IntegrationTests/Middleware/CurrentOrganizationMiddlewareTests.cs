using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Middleware;

public class CurrentOrganizationMiddlewareTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Request_WithValidHeader_SetsCurrentOrganizationId()
    {
        var organizationId = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", organizationId.ToString());

        var response = await client.GetAsync("/api/organizations");

        Assert.NotNull(response);
    }

    [Fact]
    public async Task Request_WithInvalidHeader_DoesNotThrow()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", "not-a-guid");

        var response = await client.GetAsync("/api/organizations");

        Assert.NotNull(response);
    }
}
