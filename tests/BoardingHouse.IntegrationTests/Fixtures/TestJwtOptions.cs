namespace BoardingHouse.IntegrationTests.Fixtures;

/// <summary>
/// JWT values shared between <see cref="PostgresApiFactory"/> (which configures the test host
/// with them) and any test that needs to hand-craft tokens matching that configuration.
/// </summary>
public static class TestJwtOptions
{
    public const string Secret = "this-is-a-test-secret-at-least-32-chars-long";

    // Match the defaults from appsettings.json, which PostgresApiFactory does not override.
    public const string Issuer = "BoardingHouse.Api";
    public const string Audience = "BoardingHouse.Client";
}
