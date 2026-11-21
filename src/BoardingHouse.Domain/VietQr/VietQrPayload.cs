namespace BoardingHouse.Domain.VietQr;

public static class VietQrPayload
{
    public const int AccountNumberMaxLength = 19;
    public const string InvalidAccountNumberMessage =
        "Bank account number must be 1 to 19 letters or digits when BankBin is set";

    public static bool IsValidAccountNumber(string? accountNumber) =>
        accountNumber is { Length: > 0 and <= AccountNumberMaxLength } && accountNumber.All(char.IsAsciiLetterOrDigit);
}
