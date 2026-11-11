namespace BoardingHouse.Domain.Common;

public static class BillingDayRule
{
    public const int Min = 1;
    public const int Max = 28;

    public static void EnsureValid(int? billingDay, string name)
    {
        if (billingDay is < Min or > Max)
        {
            throw new DomainException($"{name} must be between {Min} and {Max}");
        }
    }
}
