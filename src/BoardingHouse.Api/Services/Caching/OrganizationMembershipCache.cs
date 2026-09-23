using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace BoardingHouse.Api.Services.Caching;

public class OrganizationMembershipCache(IDistributedCache cache, IConfiguration configuration, ILogger<OrganizationMembershipCache> logger)
    : IOrganizationMembershipCache
{
    private TimeSpan Ttl => TimeSpan.FromSeconds(configuration.GetValue("Redis:OrganizationMembershipCacheTtlSeconds", 300));

    public async Task<List<(Guid OrganizationId, Guid RoleId)>?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        string? json;
        try
        {
            json = await cache.GetStringAsync(Key(userId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read cached memberships for user {UserId}; falling back to database", userId);
            return null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<Guid[]?>>(json)
                ?? throw new FormatException($"Cached memberships payload deserialized to null for user {userId}");
            return [.. entries.Select(Decode)];
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            logger.LogWarning(ex, "Failed to deserialize cached memberships for user {UserId}; falling back to database", userId);

            try
            {
                await cache.RemoveAsync(Key(userId), cancellationToken);
            }
            catch (Exception removeEx) when (removeEx is not OperationCanceledException)
            {
                logger.LogWarning(removeEx, "Failed to remove malformed cached memberships for user {UserId}", userId);
            }

            return null;
        }
    }

    public async Task SetAsync(Guid userId, List<(Guid OrganizationId, Guid RoleId)> memberships, CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = memberships.Select(Encode).ToList();

            await cache.SetStringAsync(
                Key(userId),
                JsonSerializer.Serialize(entries),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to cache memberships for user {UserId}; continuing without caching", userId);
        }
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(Key(userId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to remove cached memberships for user {UserId}; poisoning entry to force a database refresh", userId);

            try
            {
                await cache.SetStringAsync(
                    Key(userId),
                    "invalidated",
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                    cancellationToken);
            }
            catch (Exception poisonEx) when (poisonEx is not OperationCanceledException)
            {
                logger.LogWarning(poisonEx, "Failed to poison cached memberships for user {UserId}; stale entry may persist until TTL expiry", userId);
            }
        }

        _ = DelayedReinvalidateAsync(userId);
    }

    private async Task DelayedReinvalidateAsync(Guid userId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            await cache.RemoveAsync(Key(userId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed delayed re-invalidation of cached memberships for user {UserId}; stale entry may persist until TTL expiry", userId);
        }
    }

    private static string Key(Guid userId) => $"org-memberships:{userId}";

    private static Guid[] Encode((Guid OrganizationId, Guid RoleId) membership) => [membership.OrganizationId, membership.RoleId];

    private static (Guid OrganizationId, Guid RoleId) Decode(Guid[]? entry)
    {
        if (entry is null || entry.Length != 2)
        {
            throw new FormatException($"Invalid cached membership entry: '{JsonSerializer.Serialize(entry)}'");
        }

        return (entry[0], entry[1]);
    }
}
