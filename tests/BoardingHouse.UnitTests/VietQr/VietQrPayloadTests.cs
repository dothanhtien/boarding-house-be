using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.UnitTests.VietQr;

public class VietQrPayloadTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("0011001234567")]
    [InlineData("VQRQ0001ABC")]
    [InlineData("1234567890123456789")] // 19 characters
    public void IsValidAccountNumber_LettersOrDigitsUpTo19_ReturnsTrue(string accountNumber)
    {
        Assert.True(VietQrPayload.IsValidAccountNumber(accountNumber));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0011 001234567")]
    [InlineData("0011-001234567")]
    [InlineData("0011001234567 ")]
    [InlineData("12345678901234567890")] // 20 characters
    [InlineData("００１１")] // full-width digits are not ASCII
    public void IsValidAccountNumber_Otherwise_ReturnsFalse(string? accountNumber)
    {
        Assert.False(VietQrPayload.IsValidAccountNumber(accountNumber));
    }
}
