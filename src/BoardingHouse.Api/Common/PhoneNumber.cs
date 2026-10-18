namespace BoardingHouse.Api.Common;

public static class PhoneNumber
{
    public const int MaxLength = 20;

    // Vietnamese numbers are stored in their domestic form: separators stripped, +84 → 0.
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var compact = string.Concat(phone.Where(c => !IsSeparator(c)));
        return compact.StartsWith("+84", StringComparison.Ordinal) ? "0" + compact.Substring(3) : compact;
    }

    // Digits only, with an optional leading '+'. ASCII digits only: char.IsDigit would also accept other scripts.
    public static bool IsValid(string? phone)
    {
        if (Normalize(phone) is not { } normalized || normalized.Length > MaxLength)
        {
            return false;
        }

        var digits = normalized.StartsWith('+') ? normalized.Substring(1) : normalized;
        return digits.Length > 0 && digits.All(char.IsAsciiDigit);
    }

    // 0912345678 → 091****678. Keeps the length (it is no secret) but hides the subscriber digits.
    public static string Mask(string phone)
    {
        if (phone.Length <= 6)
        {
            var visible = Math.Min(2, phone.Length / 2);
            return new string('*', phone.Length - visible) + phone.Substring(phone.Length - visible);
        }

        return phone.Substring(0, 3) + new string('*', phone.Length - 6) + phone.Substring(phone.Length - 3);
    }

    private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c is '.' or '-' or '(' or ')';
}
