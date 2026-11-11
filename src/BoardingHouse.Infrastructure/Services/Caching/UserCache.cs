using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Distributed;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Infrastructure.Services.Caching;

public class UserCache(IDistributedCache cache, IConfiguration configuration, ILogger<UserCache> logger) : IUserCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { MapUser } }
    };

    private static readonly HashSet<string> NotCached =
    [
        nameof(User.PasswordHash),
        nameof(User.RefreshTokens),
        nameof(User.UserRoles),
        nameof(User.OrganizationMembers)
    ];

    private static void MapUser(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(User))
        {
            return;
        }

        typeInfo.CreateObject = () => (User)Activator.CreateInstance(typeof(User), nonPublic: true)!;

        foreach (var property in typeInfo.Properties.Where(p => NotCached.Contains(p.Name)).ToList())
        {
            typeInfo.Properties.Remove(property);
        }

        foreach (var property in typeInfo.Properties.Where(p => p.Set is null))
        {
            if (property.AttributeProvider is PropertyInfo info && info.GetSetMethod(nonPublic: true) is { } setter)
            {
                property.Set = (target, value) => setter.Invoke(target, [value]);
            }
        }
    }

    private TimeSpan Ttl => TimeSpan.FromSeconds(configuration.GetValue("Redis:UserCacheTtlSeconds", 30));

    public async Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        string? json;
        try
        {
            json = await cache.GetStringAsync(Key(userId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read cached user {UserId}; falling back to database", userId);
            return null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<User>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize cached user {UserId}; falling back to database", userId);
            await cache.RemoveAsync(Key(userId), cancellationToken);
            return null;
        }
    }

    public async Task SetAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetStringAsync(
                Key(user.Id),
                JsonSerializer.Serialize(user, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to cache user {UserId}; continuing without caching", user.Id);
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
            logger.LogWarning(ex, "Failed to invalidate cached user {UserId}; stale entry may persist until TTL expiry", userId);
        }
    }

    private static string Key(Guid userId) => $"user:{userId}";
}
