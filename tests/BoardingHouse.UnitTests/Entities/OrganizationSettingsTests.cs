using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.UnitTests.Entities;

public class OrganizationSettingsTests
{
    private static OrganizationSettings NewSettings() => OrganizationSettings.DefaultsFor(Guid.NewGuid());

    private static void Update(OrganizationSettings settings, OrganizationSettingsDetails details) =>
        settings.Update(details, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static OrganizationSettings NewSettingsWithBank()
    {
        var settings = NewSettings();
        Update(settings, settings.Details with { BankBin = "970436", BankAccountNumber = "0011001234567" });
        return settings;
    }

    [Fact]
    public void Defaults_BankBinIsNull()
    {
        var settings = NewSettings();

        Assert.Null(settings.BankBin);
        Assert.Null(settings.Details.BankBin);
    }

    [Fact]
    public void Update_BankBinWithAccountNumber_SetsIt()
    {
        var settings = NewSettingsWithBank();

        Assert.Equal(("970436", "0011001234567"), (settings.BankBin, settings.BankAccountNumber));
        Assert.Equal("970436", settings.Details.BankBin);
    }

    [Fact]
    public void Update_DetailsRoundTrip_KeepsTheBankBin()
    {
        var settings = NewSettingsWithBank();

        Update(settings, settings.Details with { VatRate = 8 });

        Assert.Equal("970436", settings.BankBin);
    }

    [Fact]
    public void Update_UnknownBankBin_ThrowsAndKeepsTheOldValues()
    {
        var settings = NewSettingsWithBank();

        var exception = Assert.Throws<DomainException>(() =>
            Update(settings, settings.Details with { BankBin = "000000", VatRate = 8 }));

        Assert.Equal(VietQrBanks.UnknownBinMessage, exception.Message);
        Assert.Equal(DomainErrorKind.Validation, exception.Kind);
        Assert.Equal(nameof(OrganizationSettings.BankBin), exception.Field);
        Assert.Equal("970436", settings.BankBin);
        Assert.Null(settings.VatRate);
    }

    [Fact]
    public void Update_BankBinWithoutAccountNumber_Throws()
    {
        var settings = NewSettings();

        var exception = Assert.Throws<DomainException>(() =>
            Update(settings, settings.Details with { BankBin = "970436" }));

        Assert.Equal(VietQrPayload.InvalidAccountNumberMessage, exception.Message);
        Assert.Equal(nameof(OrganizationSettings.BankAccountNumber), exception.Field);
        Assert.Null(settings.BankBin);
    }

    [Fact]
    public void Update_ClearingTheAccountNumberWhileTheBankBinIsSet_Throws()
    {
        var settings = NewSettingsWithBank();

        Assert.Throws<DomainException>(() => Update(settings, settings.Details with { BankAccountNumber = null }));

        Assert.Equal("0011001234567", settings.BankAccountNumber);
    }

    [Fact]
    public void Update_BankBinOnAnAlreadySavedFreeTextAccountNumber_Throws()
    {
        var settings = NewSettings();
        Update(settings, settings.Details with { BankAccountNumber = "0011 001 234567" });

        Assert.Throws<DomainException>(() => Update(settings, settings.Details with { BankBin = "970436" }));

        Assert.Null(settings.BankBin);
    }

    [Theory]
    [InlineData("0011 001234567")]
    [InlineData("0011-001234567")]
    [InlineData("12345678901234567890")] // 20 characters
    public void Update_BankBinWithAnAccountNumberQrCannotCarry_Throws(string accountNumber)
    {
        var settings = NewSettings();

        Assert.Throws<DomainException>(() =>
            Update(settings, settings.Details with { BankBin = "970436", BankAccountNumber = accountNumber }));
    }

    [Fact]
    public void Update_FreeTextAccountNumberWithoutBankBin_IsAllowed()
    {
        var settings = NewSettings();

        Update(settings, settings.Details with { BankAccountNumber = "0011 001 234567" });

        Assert.Equal("0011 001 234567", settings.BankAccountNumber);
    }

    [Fact]
    public void Update_ClearingOnlyTheBankBin_KeepsTheAccountNumber()
    {
        var settings = NewSettingsWithBank();

        Update(settings, settings.Details with { BankBin = null });

        Assert.Equal(((string?)null, "0011001234567"), (settings.BankBin, settings.BankAccountNumber));
    }

    [Fact]
    public void Update_ClearingBothBankBinAndAccountNumber_IsAllowed()
    {
        var settings = NewSettingsWithBank();

        Update(settings, settings.Details with { BankBin = null, BankAccountNumber = null });

        Assert.Equal(((string?)null, (string?)null), (settings.BankBin, settings.BankAccountNumber));
    }
}
