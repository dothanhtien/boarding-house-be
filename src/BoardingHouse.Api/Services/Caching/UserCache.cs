using System.Text.Json;
using BoardingHouse.Api.Entities;
using Microsoft.Extensions.Caching.Distributed;

namespace BoardingHouse.Api.Services.Caching;

public class UserCache(IDistributedCache cache, IConfiguration configuration, ILogger<UserCache> logger) : IUserCache
{
    private TimeSpan Ttl => TimeSpan.FromSeconds(configuration.GetValue("Redis:UserCacheTtlSeconds", 30));

    public async Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var json = await cache.GetStringAsync(Key(userId), cancellationToken);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<User>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize cached user {UserId}; falling back to database", userId);
            await cache.RemoveAsync(Key(userId), cancellationToken);
            return null;
        }
    }

    public Task SetAsync(User user, CancellationToken cancellationToken = default)
    {
        var toCache = new User
        {
            Id = user.Id,
            Email = user.Email,
            EmailVerifiedAt = user.EmailVerifiedAt,
            Phone = user.Phone,
            FullName = user.FullName,
            LastLoginAt = user.LastLoginAt,
            IsActive = user.IsActive,
            PasswordHash = string.Empty,
            CreatedAt = user.CreatedAt,
            CreatedBy = user.CreatedBy,
            UpdatedAt = user.UpdatedAt,
            UpdatedBy = user.UpdatedBy
        };

        return cache.SetStringAsync(
            Key(user.Id),
            JsonSerializer.Serialize(toCache),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            cancellationToken);
    }

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(Key(userId), cancellationToken);

    private static string Key(Guid userId) => $"user:{userId}";
}
