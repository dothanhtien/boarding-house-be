using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class RefreshToken : Entity
{
    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RevokedReason? RevokedReason { get; private set; }
    public Guid? ReplacedById { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt < now;

    private RefreshToken() { }

    public static RefreshToken Issue(
        Guid userId, string tokenHash, DateTimeOffset expiresAt, string? ipAddress, string? userAgent, DateTimeOffset now)
    {
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = now
        };
    }

    public void Revoke(RevokedReason reason, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    public RefreshToken RotateTo(
        string newTokenHash, DateTimeOffset expiresAt, string? ipAddress, string? userAgent, DateTimeOffset now)
    {
        if (IsRevoked || IsExpiredAt(now))
        {
            throw new InvalidOperationException("Only an active refresh token can be rotated");
        }

        var next = Issue(UserId, newTokenHash, expiresAt, ipAddress, userAgent, now);
        Revoke(Enums.RevokedReason.Rotation, now);
        ReplacedById = next.Id;

        return next;
    }
}
