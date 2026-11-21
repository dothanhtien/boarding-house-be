using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.DTOs;

public class VoidPaymentRequestValidatorTests
{
    private readonly VoidPaymentRequestValidator _validator = new();

    [Fact]
    public void Validate_Reason_HasNoErrors()
    {
        _validator.TestValidate(new VoidPaymentRequest { Reason = "Bounced transfer" }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankReason_HasError(string? reason)
    {
        _validator.TestValidate(new VoidPaymentRequest { Reason = reason! })
            .ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("Reason is required");
    }

    [Fact]
    public void Validate_ReasonTooLong_HasError()
    {
        _validator.TestValidate(new VoidPaymentRequest { Reason = new string('a', Payment.VoidReasonMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage($"Reason must not exceed {Payment.VoidReasonMaxLength} characters");
    }

    [Fact]
    public void Validate_ReasonAtMaxLengthAfterTrim_HasNoErrors()
    {
        _validator.TestValidate(new VoidPaymentRequest { Reason = $"  {new string('a', Payment.VoidReasonMaxLength)}  " })
            .ShouldNotHaveAnyValidationErrors();
    }
}
