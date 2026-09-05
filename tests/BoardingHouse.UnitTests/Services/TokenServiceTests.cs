using System.IdentityModel.Tokens.Jwt;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Services;
using Microsoft.Extensions.Configuration;

namespace BoardingHouse.UnitTests.Services;

public class TokenServiceTests
{
    private static TokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "this-is-a-test-secret-at-least-32-chars-long",
                ["Jwt:Issuer"] = "BoardingHouse.Api",
                ["Jwt:Audience"] = "BoardingHouse.Client",
                ["Jwt:AccessTokenExpirationMinutes"] = "15"
            })
            .Build();

        return new TokenService(configuration);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsDecodableJwt_WithSubClaimEqualToUserId()
    {
        var service = CreateService();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };

        var token = service.GenerateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
    }

    [Fact]
    public void HashToken_SameInput_AlwaysReturnsSameOutput()
    {
        var service = CreateService();

        var hash1 = service.HashToken("some-refresh-token");
        var hash2 = service.HashToken("some-refresh-token");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashToken_DifferentInput_ReturnsDifferentOutput()
    {
        var service = CreateService();

        var hash1 = service.HashToken("token-a");
        var hash2 = service.HashToken("token-b");

        Assert.NotEqual(hash1, hash2);
    }
}
