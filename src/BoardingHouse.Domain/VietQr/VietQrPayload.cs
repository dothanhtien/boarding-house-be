using System.Globalization;

namespace BoardingHouse.Domain.VietQr;

public static class VietQrPayload
{
    public const int AccountNumberMaxLength = 19;
    public const int TransferContentMaxLength = 25;
    public const decimal MaxAmount = 9_999_999_999_999m; // field 54 holds at most 13 characters
    public const string InvalidAccountNumberMessage =
        "Bank account number must be 1 to 19 letters or digits when BankBin is set";

    private const string PayloadFormatIndicator = "01";
    private const string DynamicPointOfInitiation = "12";
    private const string NapasGuid = "A000000727";
    private const string TransferToAccountServiceCode = "QRIBFTTA";
    private const string VndCurrencyCode = "704";
    private const string VietnamCountryCode = "VN";
    private const string CrcFieldPrefix = "6304";

    public static bool IsValidAccountNumber(string? accountNumber) =>
        accountNumber is { Length: > 0 and <= AccountNumberMaxLength } && accountNumber.All(char.IsAsciiLetterOrDigit);

    // INV-202611-0001 → INV2026110001: many banks drop or mangle anything but letters and digits
    public static string TransferContentFor(string invoiceNumber)
    {
        var content = new string(invoiceNumber.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return content.Length <= TransferContentMaxLength ? content : content[..TransferContentMaxLength];
    }

    public static string Build(string bankBin, string accountNumber, decimal amount, string transferContent)
    {
        if (VietQrBanks.Find(bankBin) is null)
        {
            throw new ArgumentException(VietQrBanks.UnknownBinMessage, nameof(bankBin));
        }

        if (!IsValidAccountNumber(accountNumber))
        {
            throw new ArgumentException(InvalidAccountNumberMessage, nameof(accountNumber));
        }

        if (amount is < 1 or > MaxAmount || amount != decimal.Truncate(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be a whole number of VND between 1 and 9999999999999");
        }

        if (transferContent.Length is 0 or > TransferContentMaxLength || !transferContent.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Transfer content must be 1 to 25 letters or digits", nameof(transferContent));
        }

        var beneficiary = Field("00", bankBin) + Field("01", accountNumber);
        var merchantAccount = Field("00", NapasGuid) + Field("01", beneficiary) + Field("02", TransferToAccountServiceCode);

        var payload =
            Field("00", PayloadFormatIndicator)
            + Field("01", DynamicPointOfInitiation)
            + Field("38", merchantAccount)
            + Field("53", VndCurrencyCode)
            + Field("54", amount.ToString("0", CultureInfo.InvariantCulture))
            + Field("58", VietnamCountryCode)
            + Field("62", Field("08", transferContent))
            + CrcFieldPrefix;

        return payload + Crc16Ccitt.Compute(payload).ToString("X4", CultureInfo.InvariantCulture);
    }

    // EMVCo TLV: 2-character id, 2-digit length, value
    private static string Field(string id, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}{value.Length:D2}{value}");
}
