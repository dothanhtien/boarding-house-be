using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BoardingHouse.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Truncates every <see cref="DateTimeOffset"/>/<see cref="DateTime"/> value being written to whole microseconds,
/// the precision of Postgres <c>timestamptz</c>. Without it, an entity keeps the full 100 ns precision of the .NET
/// clock (on Linux) in memory while the database rounds it, so a response built from the just-saved entity disagrees
/// with the same row read back later. Registered in <see cref="AppDbContext.OnConfiguring"/> so it runs after the
/// interceptors passed through the options (e.g. the audit interceptor that stamps <c>CreatedAt</c>/<c>UpdatedAt</c>).
/// </summary>
public sealed class TimestampPrecisionSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly TimestampPrecisionSaveChangesInterceptor Instance = new();

    private TimestampPrecisionSaveChangesInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        TruncateTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        TruncateTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void TruncateTimestamps(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            foreach (var property in entry.Properties)
            {
                // Values read from the database are already whole microseconds, so untouched properties of a
                // Modified entity stay unchanged and are not marked as modified
                switch (property.CurrentValue)
                {
                    case DateTimeOffset value when value.Ticks % TimeSpan.TicksPerMicrosecond != 0:
                        property.CurrentValue = value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
                        break;
                    case DateTime value when value.Ticks % TimeSpan.TicksPerMicrosecond != 0:
                        property.CurrentValue = value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
                        break;
                }
            }
        }
    }
}
