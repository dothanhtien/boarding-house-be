namespace BoardingHouse.Api.Common;

public static class EnumerableExtensions
{
    public static bool AllUnique<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
    {
        var seen = new HashSet<T>(comparer);
        return source.All(seen.Add);
    }
}
