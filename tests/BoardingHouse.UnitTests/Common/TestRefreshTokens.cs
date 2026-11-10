using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Common;

// RefreshToken's constructor is private: tests issue tokens like AuthService does
internal static class TestRefreshTokens
{
    public static RefreshToken New(
        Guid userId, string tokenHash, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null)
    {
        var token = RefreshToken.Issue(
            userId, tokenHash, expiresAt ?? DateTimeOffset.UtcNow.AddDays(1), ipAddress: null, userAgent: null,
            DateTimeOffset.UtcNow);
        if (revokedAt is { } at)
        {
            token.Revoke(RevokedReason.Logout, at);
        }

        return token;
    }
}
