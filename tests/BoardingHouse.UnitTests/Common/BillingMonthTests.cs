using BoardingHouse.Domain.Common;

namespace BoardingHouse.UnitTests.Common;

public class BillingMonthTests
{
    [Theory]
    [InlineData("2026-06-01", "2026-06-01")]
    [InlineData("2026-06-15", "2026-06-01")]
    [InlineData("2026-12-31", "2026-12-01")]
    public void Of_ReturnsFirstDayOfMonth(string date, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BillingMonth.Of(DateOnly.Parse(date)).FirstDay);
    }

    [Theory]
    [InlineData("2026-06-01", true)]
    [InlineData("2026-06-02", false)]
    [InlineData("2026-06-30", false)]
    public void IsFirstDay_OnlyAcceptsTheFirstDay(string date, bool expected)
    {
        Assert.Equal(expected, BillingMonth.IsFirstDay(DateOnly.Parse(date)));
    }

    [Fact]
    public void FromFirstDay_NotTheFirstDay_Throws()
    {
        Assert.Throws<DomainException>(() => BillingMonth.FromFirstDay(new DateOnly(2026, 6, 2)));
    }

    [Theory]
    [InlineData("2026-06-01", "2026-06-30")]
    [InlineData("2028-02-01", "2028-02-29")]
    [InlineData("9999-12-01", "9999-12-31")]
    public void LastDay_ReturnsTheLastDayOfTheMonth(string month, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BillingMonth.Of(DateOnly.Parse(month)).LastDay);
    }

    [Theory]
    [InlineData("2026-06-30", "2026-07-01", 1)]
    [InlineData("2026-12-15", "2027-01-01", 1)]
    [InlineData("2026-07-01", "2026-07-01", 0)]
    [InlineData("2026-08-01", "2026-07-01", -1)]
    [InlineData("0001-01-01", "9999-12-01", 119987)]
    public void MonthsUntil_CountsCalendarMonths(string from, string to, int expected)
    {
        Assert.Equal(expected, BillingMonth.Of(DateOnly.Parse(from)).MonthsUntil(BillingMonth.Of(DateOnly.Parse(to))));
    }

    [Fact]
    public void Comparison_FollowsTheCalendar()
    {
        var june = BillingMonth.Of(new DateOnly(2026, 6, 15));
        var july = june.AddMonths(1);

        Assert.True(july >= june);
        Assert.True(june <= july);
        Assert.False(june >= july);
        Assert.Equal(BillingMonth.Of(new DateOnly(2026, 7, 31)), july);
        Assert.Equal("2026-07", july.ToString());
    }

    [Fact]
    public void StrictComparison_FollowsTheCalendar()
    {
        var june = BillingMonth.Of(new DateOnly(2026, 6, 15));
        var july = june.AddMonths(1);

        Assert.True(june < july);
        Assert.True(july > june);
        Assert.False(june < june);
        Assert.False(june > june);
    }

    [Fact]
    public void StrictComparison_WithNull_IsFalse()
    {
        var june = BillingMonth.Of(new DateOnly(2026, 6, 15));
        BillingMonth? none = null;

        Assert.False(june > none);
        Assert.False(june < none);
    }
}
