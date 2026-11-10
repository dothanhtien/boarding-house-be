namespace BoardingHouse.Domain.Common;

public readonly record struct BillingMonth
{
    private BillingMonth(DateOnly firstDay) => FirstDay = firstDay;

    public DateOnly FirstDay { get; }
    public DateOnly LastDay => new(FirstDay.Year, FirstDay.Month, DateTime.DaysInMonth(FirstDay.Year, FirstDay.Month));

    public static BillingMonth Of(DateOnly date) => new(new DateOnly(date.Year, date.Month, 1));

    public static BillingMonth FromFirstDay(DateOnly firstDay) =>
        IsFirstDay(firstDay)
            ? new BillingMonth(firstDay)
            : throw new DomainException($"{firstDay:yyyy-MM-dd} is not the first day of a month");

    public static bool IsFirstDay(DateOnly date) => date.Day == 1;

    public BillingMonth AddMonths(int months) => new(FirstDay.AddMonths(months));

    public int MonthsUntil(BillingMonth other) =>
        (other.FirstDay.Year - FirstDay.Year) * 12 + other.FirstDay.Month - FirstDay.Month;

    public static bool operator >=(BillingMonth left, BillingMonth right) => left.FirstDay >= right.FirstDay;
    public static bool operator <=(BillingMonth left, BillingMonth right) => left.FirstDay <= right.FirstDay;

    public override string ToString() => FirstDay.ToString("yyyy-MM");
}
