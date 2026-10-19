using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using BoardingHouse.Application.Services.Throttling;

namespace BoardingHouse.Api.Services.Throttling;

public class DistributedAttemptLimiter(IDistributedCache cache, ILogger<DistributedAttemptLimiter> logger) : IAttemptLimiter
{
    private sealed record Window(int Count, DateTimeOffset ExpiresAt);

    public async Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"attempts:{key}";
        try
        {
            var json = await cache.GetStringAsync(cacheKey, cancellationToken);
            var current = json is null ? null : JsonSerializer.Deserialize<Window>(json);
            var now = DateTimeOffset.UtcNow;

            if (current is null || current.ExpiresAt <= now)
            {
                current = new Window(0, now.Add(window));
            }

            if (current.Count >= limit)
            {
                return false;
            }

            await cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(current with { Count = current.Count + 1 }),
                new DistributedCacheEntryOptions { AbsoluteExpiration = current.ExpiresAt },
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to check attempt limit for {Key}; allowing the attempt", cacheKey);
            return true;
        }
    }
}
