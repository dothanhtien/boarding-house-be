namespace BoardingHouse.Api.Services.Throttling;

/// <summary>
/// Fixed-window attempt counter shared across instances (Redis-backed). Use it to cap actions whose
/// outcome leaks information, e.g. probing whether a value is already taken by someone else.
/// </summary>
public interface IAttemptLimiter
{
    /// <summary>
    /// Records one attempt under <paramref name="key"/>. Returns false, without recording, when
    /// <paramref name="limit"/> attempts were already made in the current window.
    /// </summary>
    Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken = default);
}
