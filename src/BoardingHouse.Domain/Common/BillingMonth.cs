namespace BoardingHouse.Domain.Common;

public static class BillingMonth
{
    public static DateOnly Of(DateOnly date) => new(date.Year, date.Month, 1);
    public static bool IsFirstDay(DateOnly month) => month.Day == 1;
}
