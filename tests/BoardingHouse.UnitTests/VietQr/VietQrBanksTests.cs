using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.UnitTests.VietQr;

public class VietQrBanksTests
{
    [Fact]
    public void All_BinsAreSixDigitsAndUnique()
    {
        Assert.All(VietQrBanks.All, bank => Assert.Matches("^[0-9]{6}$", bank.Bin));
        Assert.Equal(VietQrBanks.All.Count, VietQrBanks.All.Select(b => b.Bin).Distinct().Count());
    }

    [Fact]
    public void All_CodesAreUnique()
    {
        Assert.Equal(VietQrBanks.All.Count, VietQrBanks.All.Select(b => b.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_CodesAndShortNamesAreFilledAndAscii()
    {
        Assert.All(VietQrBanks.All, bank =>
        {
            Assert.False(string.IsNullOrWhiteSpace(bank.Code));
            Assert.False(string.IsNullOrWhiteSpace(bank.ShortName));
            Assert.True(bank.ShortName.All(char.IsAscii), $"{bank.ShortName} must be ASCII");
        });
    }

    [Theory]
    [InlineData("970436", "VCB")]
    [InlineData("970422", "MB")]
    [InlineData("970418", "BIDV")]
    [InlineData("970415", "ICB")]
    public void Find_KnownBin_ReturnsTheBank(string bin, string code)
    {
        Assert.Equal(code, VietQrBanks.Find(bin)?.Code);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("971025")] // MoMo: an e-wallet, left out on purpose
    [InlineData("971101")] // ZaloPay
    [InlineData("97043")]
    [InlineData(" 970436")]
    [InlineData("")]
    public void Find_UnknownBin_ReturnsNull(string bin)
    {
        Assert.Null(VietQrBanks.Find(bin));
    }
}
