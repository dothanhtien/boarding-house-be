namespace BoardingHouse.Domain.Common;

public static class BillingMonth
{
    public static DateOnly Of(DateOnly date) => new(date.Year, date.Month, 1);
    public static bool IsFirstDay(DateOnly month) => month.Day == 1;
    public static DateOnly LastDay(DateOnly month) =>
        new(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
    public static int MonthsBetween(DateOnly from, DateOnly to) =>
        (to.Year - from.Year) * 12 + to.Month - from.Month;
}
