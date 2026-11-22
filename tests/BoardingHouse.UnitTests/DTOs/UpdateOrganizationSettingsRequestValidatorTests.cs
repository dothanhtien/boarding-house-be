using FluentValidation.TestHelper;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateOrganizationSettingsRequestValidatorTests
{
    private readonly UpdateOrganizationSettingsRequestValidator _validator = new();

    private static UpdateOrganizationSettingsRequest ValidRequest() => new() { VatRate = 8 };

    [Fact]
    public void Validate_AllFieldsNull_IsValid()
    {
        var result = _validator.TestValidate(new UpdateOrganizationSettingsRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_OnlyOneFieldProvided_IsValid()
    {
        var result = _validator.TestValidate(new UpdateOrganizationSettingsRequest { DefaultBillingDay = 5 });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DefaultBillingDayZero_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultBillingDay = 0 });

        result.ShouldHaveValidationErrorFor(x => x.DefaultBillingDay);
    }

    [Fact]
    public void Validate_DefaultBillingDay29_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultBillingDay = 29 });

        result.ShouldHaveValidationErrorFor(x => x.DefaultBillingDay);
    }

    [Fact]
    public void Validate_DefaultBillingDay28_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultBillingDay = 28 });

        result.ShouldNotHaveValidationErrorFor(x => x.DefaultBillingDay);
    }

    [Fact]
    public void Validate_OnlyLateFeeTypeSet_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeType = LateFeeType.Fixed });

        result.ShouldHaveValidationErrorFor(x => x.LateFeeValue);
    }

    [Fact]
    public void Validate_OnlyLateFeeValueSet_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeValue = 50000 });

        result.ShouldHaveValidationErrorFor(x => x.LateFeeValue);
    }

    [Fact]
    public void Validate_LateFeeTypeAndValueBothSet_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeType = LateFeeType.Fixed, LateFeeValue = 50000 });

        result.ShouldNotHaveValidationErrorFor(x => x.LateFeeValue);
    }

    [Fact]
    public void Validate_LateFeeGraceDaysNegative_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeGraceDays = -1 });

        result.ShouldHaveValidationErrorFor(x => x.LateFeeGraceDays);
    }

    [Fact]
    public void Validate_VatRateExceeds100_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { VatRate = 150 });

        result.ShouldHaveValidationErrorFor(x => x.VatRate);
    }

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_KnownBankBin_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { BankBin = "970436" });

        result.ShouldNotHaveValidationErrorFor(x => x.BankBin);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("97043")]
    [InlineData("VCB")]
    [InlineData("")]
    [InlineData("971025")] // MoMo: an e-wallet
    public void Validate_UnknownBankBin_HasError(string bankBin)
    {
        var result = _validator.TestValidate(ValidRequest() with { BankBin = bankBin });

        result.ShouldHaveValidationErrorFor(x => x.BankBin).WithErrorMessage(VietQrBanks.UnknownBinMessage);
    }

    [Fact]
    public void Validate_BankBinNull_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { BankBin = new Optional<string>(null) });

        result.ShouldNotHaveValidationErrorFor(x => x.BankBin);
    }

    [Fact]
    public void Validate_BankBinWithoutAccountNumber_LeavesThePairToTheEntity()
    {
        // The account number may already be saved: only the merged state can tell
        var result = _validator.TestValidate(new UpdateOrganizationSettingsRequest { BankBin = "970436" });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
