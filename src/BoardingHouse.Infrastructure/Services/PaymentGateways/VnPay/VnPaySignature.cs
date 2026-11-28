using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BoardingHouse.Infrastructure.Services.PaymentGateways.VnPay;

public static class VnPaySignature
{
    public const string SecureHashField = "vnp_SecureHash";
    public const string SecureHashTypeField = "vnp_SecureHashType";

    private const string FieldPrefix = "vnp_";

    public static string BuildData(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields
            .Where(f => f.Key.StartsWith(FieldPrefix, StringComparison.Ordinal)
                && f.Key is not (SecureHashField or SecureHashTypeField)
                && !string.IsNullOrEmpty(f.Value))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{WebUtility.UrlEncode(f.Key)}={WebUtility.UrlEncode(f.Value)}"));

    public static string Sign(string data, string secret) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));

    public static bool Verify(IReadOnlyDictionary<string, string> fields, string secret)
    {
        if (!fields.TryGetValue(SecureHashField, out var received) || string.IsNullOrEmpty(received))
        {
            return false;
        }

        var expected = Sign(BuildData(fields), secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(received.ToLowerInvariant()));
    }
}
