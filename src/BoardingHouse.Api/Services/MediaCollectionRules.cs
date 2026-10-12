using BoardingHouse.Api.Exceptions;

namespace BoardingHouse.Api.Services;

public static class MediaCollectionRules
{
    public static void EnsureWithinLimit(int currentCount, int addedCount, int max, string message)
    {
        if (currentCount + addedCount > max)
        {
            throw new AppValidationException(message);
        }
    }

    public static int NextSortOrder(IEnumerable<int> sortOrders) => sortOrders.DefaultIfEmpty(-1).Max() + 1;
}
