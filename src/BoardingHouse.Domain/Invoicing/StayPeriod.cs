using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Invoicing;

public sealed record StayPeriod(DateOnly From, DateOnly To, int Days, int DaysInMonth)
{
    public bool IsFullMonth => Days == DaysInMonth;

    public string Suffix => IsFullMonth ? "" : $" ({Days}/{DaysInMonth} days)";

    public decimal Prorate(decimal monthlyAmount) =>
        Money.Round(IsFullMonth ? monthlyAmount : monthlyAmount * Days / DaysInMonth);

    public static StayPeriod Of(BillingMonth month, DateOnly startDate, DateOnly? lastDayOfStay)
    {
        var from = startDate > month.FirstDay ? startDate : month.FirstDay;
        var to = lastDayOfStay is { } last && last < month.LastDay ? last : month.LastDay;

        return new StayPeriod(from, to, to.DayNumber - from.DayNumber + 1, month.LastDay.Day);
    }
}
