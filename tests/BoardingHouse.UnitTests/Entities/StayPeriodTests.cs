using BoardingHouse.Domain.Common;

namespace BoardingHouse.UnitTests.Entities;

public class StayPeriodTests
{
    private static readonly BillingMonth June = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));

    [Theory]
    [InlineData("2026-06-30", null, true)]          // moved in on the last day
    [InlineData("2026-07-01", null, false)]         // moved in the month after
    [InlineData("2026-01-01", "2026-06-01", true)]  // left on the first day
    [InlineData("2026-01-01", "2026-05-31", false)] // left the month before
    [InlineData("2026-06-10", "2026-06-20", true)]  // whole stay inside the month
    public void Covers_MonthWithAtLeastOneDayOfStay(string from, string? to, bool covered)
    {
        var stay = new StayPeriod(DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to));

        Assert.Equal(covered, stay.Covers(June));
    }
}
