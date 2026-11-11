using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.Entities;

public class UtilityReadingTests
{
    private static readonly BillingMonth October = BillingMonth.Of(new DateOnly(2026, 10, 1));
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid RoomId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();

    private static UtilityReading Record(
        BillingMonth month, DateOnly readOn, decimal currentValue, UtilityReading? latest = null, decimal? initial = null) =>
        UtilityReading.Record(RoomId, ServiceId, readOn, month, latest, initial, currentValue, billedLeaseId: null, Now, SentinelActors.System);

    [Fact]
    public void Record_First_UsesInitialPreviousValue()
    {
        var reading = Record(October, new DateOnly(2026, 10, 30), 120, initial: 100);

        Assert.Equal((100m, 120m, October), (reading.PreviousValue, reading.CurrentValue, reading.ReadingMonth));
    }

    [Fact]
    public void Record_Next_ChainsFromLatest()
    {
        var latest = Record(October, new DateOnly(2026, 10, 30), 120);

        var next = Record(October.AddMonths(1), new DateOnly(2026, 11, 30), 150, latest, initial: 999);

        Assert.Equal(120, next.PreviousValue);
    }

    [Fact]
    public void Record_SameMonthAgain_Throws()
    {
        var latest = Record(October, new DateOnly(2026, 10, 30), 120);

        var ex = Assert.Throws<DomainException>(() => Record(October, new DateOnly(2026, 10, 31), 130, latest));

        Assert.Equal("A reading already exists for this month — delete it to enter it again", ex.Message);
    }

    [Fact]
    public void Record_EarlierMonth_Throws()
    {
        var latest = Record(October, new DateOnly(2026, 10, 30), 120);

        var ex = Assert.Throws<DomainException>(() => Record(October.AddMonths(-1), new DateOnly(2026, 10, 30), 130, latest));

        Assert.Equal("A reading for a later month (2026-10) already exists", ex.Message);
    }

    [Fact]
    public void Record_ReadOnGoesBack_Throws()
    {
        var latest = Record(October, new DateOnly(2026, 10, 30), 120);

        Assert.Throws<DomainException>(() => Record(October.AddMonths(1), new DateOnly(2026, 10, 29), 130, latest));
    }

    [Fact]
    public void Record_ValueGoesDown_ThrowsOnCurrentValue()
    {
        var latest = Record(October, new DateOnly(2026, 10, 30), 120);

        var ex = Assert.Throws<DomainException>(() => Record(October.AddMonths(1), new DateOnly(2026, 11, 30), 119, latest));

        Assert.Equal(nameof(UtilityReading.CurrentValue), ex.Field);
    }
}
