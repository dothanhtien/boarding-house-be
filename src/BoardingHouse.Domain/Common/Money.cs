namespace BoardingHouse.Domain.Common;

public static class Money
{
    public static decimal Round(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);
}
