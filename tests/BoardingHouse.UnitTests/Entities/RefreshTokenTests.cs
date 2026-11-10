using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken() =>
        RefreshToken.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), "127.0.0.1", "agent", Now);

    [Fact]
    public void Issue_SetsFieldsAndIsNotRevoked()
    {
        var token = NewToken();

        Assert.Equal(("hash", Now.AddDays(7), "127.0.0.1", "agent", Now),
            (token.TokenHash, token.ExpiresAt, token.IpAddress, token.UserAgent, token.CreatedAt));
        Assert.False(token.IsRevoked);
        Assert.False(token.IsExpiredAt(Now));
        Assert.True(token.IsExpiredAt(Now.AddDays(8)));
    }

    [Fact]
    public void Revoke_AlreadyRevoked_KeepsTheFirstReason()
    {
        var token = NewToken();

        token.Revoke(RevokedReason.Logout, Now);
        token.Revoke(RevokedReason.Suspicious, Now.AddMinutes(1));

        Assert.Equal(((DateTimeOffset?)Now, (RevokedReason?)RevokedReason.Logout), (token.RevokedAt, token.RevokedReason));
    }

    [Fact]
    public void RotateTo_RevokesAndLinksToTheNextToken()
    {
        var token = NewToken();
        var later = Now.AddHours(1);

        var next = token.RotateTo("next-hash", later.AddDays(7), "10.0.0.1", "agent-2", later);

        Assert.Equal((token.UserId, "next-hash", later), (next.UserId, next.TokenHash, next.CreatedAt));
        Assert.Equal(((DateTimeOffset?)later, (RevokedReason?)RevokedReason.Rotation, (Guid?)next.Id),
            (token.RevokedAt, token.RevokedReason, token.ReplacedById));
    }

    [Fact]
    public void RotateTo_RevokedOrExpired_Throws()
    {
        var revoked = NewToken();
        revoked.Revoke(RevokedReason.Logout, Now);

        Assert.Throws<InvalidOperationException>(() => revoked.RotateTo("x", Now.AddDays(7), null, null, Now));
        Assert.Throws<InvalidOperationException>(() => NewToken().RotateTo("x", Now.AddDays(30), null, null, Now.AddDays(8)));
    }
}
