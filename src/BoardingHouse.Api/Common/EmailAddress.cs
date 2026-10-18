namespace BoardingHouse.Api.Common;

public static class EmailAddress
{
    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    // nguyenvana@gmail.com → ng***@gmail.com. Fixed-length mask so the local part's length does not leak.
    public static string Mask(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var visible = at <= 2 ? 1 : 2;
        return email.Substring(0, visible) + "***" + email.Substring(at);
    }
}
