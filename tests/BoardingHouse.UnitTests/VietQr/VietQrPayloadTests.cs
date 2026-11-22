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

    [Theory]
    [InlineData("970436", "0011001234567", 3_000_000, "INV2026110001",
        "00020101021238570010A00000072701270006970436011300110012345670208QRIBFTTA5303704540730000005802VN62170813INV202611000163044191")]
    [InlineData("970436", "0011001234567", 1_800_000, "INV2026110001",
        "00020101021238570010A00000072701270006970436011300110012345670208QRIBFTTA5303704540718000005802VN62170813INV202611000163044A39")]
    [InlineData("970422", "0123456789", 2_000_000, "INV2026110001",
        "00020101021238540010A00000072701240006970422011001234567890208QRIBFTTA5303704540720000005802VN62170813INV2026110001630465D9")]
    public void Build_ReturnsTheExpectedPayload(string bankBin, string accountNumber, decimal amount, string content, string expected)
    {
        Assert.Equal(expected, VietQrPayload.Build(bankBin, accountNumber, amount, content));
    }

    [Fact]
    public void Build_HasTheVietQrFieldsInOrder()
    {
        var payload = VietQrPayload.Build("970436", "0011001234567", 3_000_000, "INV2026110001");

        var fields = ParseTlv(payload);
        Assert.Equal(["00", "01", "38", "53", "54", "58", "62", "63"], fields.Select(f => f.Id));
        Assert.Equal("01", fields[0].Value);
        Assert.Equal("12", fields[1].Value);
        Assert.Equal("704", fields[3].Value);
        Assert.Equal("3000000", fields[4].Value);
        Assert.Equal("VN", fields[5].Value);

        var merchantAccount = ParseTlv(fields[2].Value);
        Assert.Equal([("00", "A000000727"), ("02", "QRIBFTTA")], merchantAccount.Where(f => f.Id != "01"));
        Assert.Equal([("00", "970436"), ("01", "0011001234567")], ParseTlv(merchantAccount.Single(f => f.Id == "01").Value));

        Assert.Equal([("08", "INV2026110001")], ParseTlv(fields[6].Value));
    }

    [Fact]
    public void Build_EndsWithTheCrcOfEverythingBeforeIt()
    {
        var payload = VietQrPayload.Build("970422", "0123456789", 2_000_000, "INV2026110001");

        var body = payload[..^4];
        Assert.EndsWith("6304", body);
        Assert.Equal(Crc16Ccitt.Compute(body).ToString("X4"), payload[^4..]);
    }

    [Fact]
    public void Build_EveryListedBank_ProducesAPayloadWithItsBin()
    {
        Assert.All(VietQrBanks.All, bank =>
        {
            var payload = VietQrPayload.Build(bank.Bin, "0011001234567", 1_000, "INV2026110001");

            var merchantAccount = ParseTlv(ParseTlv(payload).Single(f => f.Id == "38").Value);
            var beneficiary = ParseTlv(merchantAccount.Single(f => f.Id == "01").Value);
            Assert.Equal(("00", bank.Bin), beneficiary[0]);
        });
    }

    [Fact]
    public void Build_AccountNumberWithLetters_IsKeptAsIs()
    {
        var payload = VietQrPayload.Build("970422", "VQRQ0001ABC", 1_000, "INV2026110001");

        var merchantAccount = ParseTlv(ParseTlv(payload).Single(f => f.Id == "38").Value);
        Assert.Contains(("01", "VQRQ0001ABC"), ParseTlv(merchantAccount.Single(f => f.Id == "01").Value));
    }

    [Fact]
    public void Build_AmountHasNoDecimalPart()
    {
        var payload = VietQrPayload.Build("970436", "0011001234567", 1_500_000.00m, "INV2026110001");

        Assert.Equal("1500000", ParseTlv(payload).Single(f => f.Id == "54").Value);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(9_999_999_999_999, "9999999999999")] // 13 characters, the field 54 maximum
    public void Build_AmountAtTheLimits_IsAccepted(decimal amount, string expected)
    {
        var payload = VietQrPayload.Build("970436", "0011001234567", amount, "INV2026110001");

        Assert.Equal(expected, ParseTlv(payload).Single(f => f.Id == "54").Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000.5)]
    [InlineData(0.5)]
    [InlineData(10_000_000_000_000)]
    public void Build_InvalidAmount_Throws(decimal amount)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            VietQrPayload.Build("970436", "0011001234567", amount, "INV2026110001"));

        Assert.Equal("amount", exception.ParamName);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("971025")] // MoMo
    [InlineData("")]
    public void Build_UnknownBin_Throws(string bankBin)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VietQrPayload.Build(bankBin, "0011001234567", 1_000, "INV2026110001"));

        Assert.Equal("bankBin", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0011 001234567")]
    [InlineData("12345678901234567890")]
    public void Build_InvalidAccountNumber_Throws(string accountNumber)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VietQrPayload.Build("970436", accountNumber, 1_000, "INV2026110001"));

        Assert.Equal("accountNumber", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("INV-202611-0001")]
    [InlineData("INV 2026110001")]
    [InlineData("HOÁĐƠN1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ")] // 26 characters
    public void Build_InvalidTransferContent_Throws(string content)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VietQrPayload.Build("970436", "0011001234567", 1_000, content));

        Assert.Equal("transferContent", exception.ParamName);
    }

    [Fact]
    public void Build_TransferContentOf25Characters_IsAccepted()
    {
        var payload = VietQrPayload.Build("970436", "0011001234567", 1_000, "ABCDEFGHIJKLMNOPQRSTUVWXY");

        Assert.Equal([("08", "ABCDEFGHIJKLMNOPQRSTUVWXY")], ParseTlv(ParseTlv(payload).Single(f => f.Id == "62").Value));
    }

    [Theory]
    [InlineData("INV-202611-0001", "INV2026110001")]
    [InlineData("INV-2026-11-0001", "INV2026110001")]
    [InlineData("inv_202611/0001 ", "INV2026110001")]
    [InlineData("INV-202611-0001-ABCDEFGHIJKLMNOP", "INV2026110001ABCDEFGHIJKL")]
    public void TransferContentFor_KeepsOnlyLettersAndDigitsUpToTheLimit(string invoiceNumber, string expected)
    {
        Assert.Equal(expected, VietQrPayload.TransferContentFor(invoiceNumber));
    }

    [Fact]
    public void TransferContentFor_ResultIsAcceptedByBuild()
    {
        var content = VietQrPayload.TransferContentFor("INV-202611-0001-ABCDEFGHIJKLMNOP");

        var payload = VietQrPayload.Build("970436", "0011001234567", 1_000, content);

        Assert.Contains(content, payload);
    }

    private static List<(string Id, string Value)> ParseTlv(string data)
    {
        var fields = new List<(string Id, string Value)>();
        var position = 0;
        while (position < data.Length)
        {
            var id = data.Substring(position, 2);
            var length = int.Parse(data.Substring(position + 2, 2));
            fields.Add((id, data.Substring(position + 4, length)));
            position += 4 + length;
        }

        return fields;
    }
}
