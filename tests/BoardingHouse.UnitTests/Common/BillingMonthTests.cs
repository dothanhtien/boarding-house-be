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
        Assert.Equal(DateOnly.Parse(expected), BillingMonth.Of(DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("2026-06-01", true)]
    [InlineData("2026-06-02", false)]
    [InlineData("2026-06-30", false)]
    public void IsFirstDay_OnlyAcceptsTheFirstDay(string date, bool expected)
    {
        Assert.Equal(expected, BillingMonth.IsFirstDay(DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("2026-06-01", "2026-06-30")]
    [InlineData("2028-02-01", "2028-02-29")]
    [InlineData("9999-12-01", "9999-12-31")]
    public void LastDay_ReturnsTheLastDayOfTheMonth(string month, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BillingMonth.LastDay(DateOnly.Parse(month)));
    }

    [Theory]
    [InlineData("2026-06-30", "2026-07-01", 1)]
    [InlineData("2026-12-15", "2027-01-01", 1)]
    [InlineData("2026-07-01", "2026-07-01", 0)]
    [InlineData("2026-08-01", "2026-07-01", -1)]
    [InlineData("0001-01-01", "9999-12-01", 119987)]
    public void MonthsBetween_CountsCalendarMonths(string from, string to, int expected)
    {
        Assert.Equal(expected, BillingMonth.MonthsBetween(DateOnly.Parse(from), DateOnly.Parse(to)));
    }
}
