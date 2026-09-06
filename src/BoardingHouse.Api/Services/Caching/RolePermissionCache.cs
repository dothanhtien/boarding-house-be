using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace BoardingHouse.Api.Services.Caching;

public class RolePermissionCache(IDistributedCache cache, IConfiguration configuration, ILogger<RolePermissionCache> logger)
    : IRolePermissionCache
{
    private TimeSpan Ttl => TimeSpan.FromSeconds(configuration.GetValue("Redis:RolePermissionsCacheTtlSeconds", 300));

    public async Task<List<(string Resource, string Action)>?> GetAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        string? json;
        try
        {
            json = await cache.GetStringAsync(Key(roleId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read cached permissions for role {RoleId}; falling back to database", roleId);
            return null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<string[]>>(json) ?? [];
            return [.. entries.Select(Decode)];
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            logger.LogWarning(ex, "Failed to deserialize cached permissions for role {RoleId}; falling back to database", roleId);
            await cache.RemoveAsync(Key(roleId), cancellationToken);
            return null;
        }
    }

    public async Task SetAsync(Guid roleId, List<(string Resource, string Action)> permissions, CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = permissions.Select(Encode).ToList();

            await cache.SetStringAsync(
                Key(roleId),
                JsonSerializer.Serialize(entries),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to cache permissions for role {RoleId}; continuing without caching", roleId);
        }
    }

    public async Task InvalidateAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(Key(roleId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to invalidate cached permissions for role {RoleId}; stale entry may persist until TTL expiry", roleId);
        }
    }

    private static string Key(Guid roleId) => $"role-permissions:{roleId}";

    private static string[] Encode((string Resource, string Action) permission) => [permission.Resource, permission.Action];

    private static (string Resource, string Action) Decode(string[] entry)
    {
        if (entry.Length != 2)
        {
            throw new FormatException($"Invalid cached permission entry: '{JsonSerializer.Serialize(entry)}'");
        }

        return (entry[0], entry[1]);
    }
}
