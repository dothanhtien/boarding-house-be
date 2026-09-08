namespace BoardingHouse.Api.Common;

public static class LikePattern
{
    /// <summary>
    /// Escape character to pass as the 3rd argument to <c>EF.Functions.ILike</c>. Npgsql's
    /// 2-argument overload emits <c>ESCAPE ''</c> (no escaping at all), so the pattern built by
    /// <see cref="Contains"/> only matches literally when this is passed explicitly.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// Builds a "contains" ILIKE pattern from raw user input, escaping the LIKE wildcards
    /// (%, _) and the escape character itself (\) so search text containing them is matched
    /// literally instead of being interpreted as a wildcard by Postgres.
    /// </summary>
    public static string Contains(string value) =>
        $"%{value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
