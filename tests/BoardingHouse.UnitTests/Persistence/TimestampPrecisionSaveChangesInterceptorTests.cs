using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Persistence;

public class TimestampPrecisionSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset SubMicrosecond =
        new DateTimeOffset(2026, 10, 12, 15, 36, 34, TimeSpan.Zero).AddTicks(3_426_879);

    private static readonly DateTimeOffset Truncated =
        new DateTimeOffset(2026, 10, 12, 15, 36, 34, TimeSpan.Zero).AddTicks(3_426_870);

    private static AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options;

        return new AppDbContext(options);
    }

    private static RoomAssetConditionHistory NewHistory(DateTimeOffset changedAt) =>
        new(Guid.NewGuid(), oldCondition: null, AssetCondition.Good, note: null, photos: [], Guid.NewGuid(), changedAt);

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_TruncatesTimestampsToMicroseconds()
    {
        using var context = CreateContext();
        var history = NewHistory(SubMicrosecond);
        context.RoomAssetConditionHistories.Add(history);

        await context.SaveChangesAsync();

        Assert.Equal(Truncated, history.ChangedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_TruncatesTimestamps()
    {
        using var context = CreateContext();
        var history = NewHistory(Truncated);
        context.RoomAssetConditionHistories.Add(history);
        await context.SaveChangesAsync();

        history.UpdatedAt = SubMicrosecond;
        context.ChangeTracker.DetectChanges();
        await context.SaveChangesAsync();

        Assert.Equal(Truncated, history.UpdatedAt);
        Assert.Equal(Truncated, history.ChangedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_TimestampStampedByOptionsInterceptor_IsTruncatedToo()
    {
        using var context = CreateContext(new StampChangedAtInterceptor());
        var history = NewHistory(Truncated);
        context.RoomAssetConditionHistories.Add(history);

        await context.SaveChangesAsync();

        Assert.Equal(Truncated, history.ChangedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WholeMicrosecondValue_IsUnchanged()
    {
        using var context = CreateContext();
        var history = NewHistory(Truncated);
        context.RoomAssetConditionHistories.Add(history);

        await context.SaveChangesAsync();

        Assert.Equal(Truncated, history.ChangedAt);
    }

    private sealed class StampChangedAtInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<RoomAssetConditionHistory>())
            {
                entry.Property(h => h.ChangedAt).CurrentValue = SubMicrosecond;
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
