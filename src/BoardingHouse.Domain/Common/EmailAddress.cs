namespace BoardingHouse.Domain.Common;

public static class EmailAddress
{
    public static string? Normalize(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    // e.g. nguyenvana@gmail.com → ng***@gmail.com
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
