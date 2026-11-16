using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.DTOs;

public class CancelInvoiceRequestValidatorTests
{
    private readonly CancelInvoiceRequestValidator _validator = new();

    [Fact]
    public void Validate_Reason_HasNoErrors()
    {
        _validator.TestValidate(new CancelInvoiceRequest { Reason = "Wrong meter reading" }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankReason_HasError(string? reason)
    {
        _validator.TestValidate(new CancelInvoiceRequest { Reason = reason! })
            .ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("Reason is required");
    }

    [Fact]
    public void Validate_ReasonTooLong_HasError()
    {
        _validator.TestValidate(new CancelInvoiceRequest { Reason = new string('a', Invoice.CancelReasonMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage($"Reason must not exceed {Invoice.CancelReasonMaxLength} characters");
    }

    [Fact]
    public void Validate_ReasonAtMaxLengthAfterTrim_HasNoErrors()
    {
        _validator.TestValidate(new CancelInvoiceRequest { Reason = $"  {new string('a', Invoice.CancelReasonMaxLength)}  " })
            .ShouldNotHaveAnyValidationErrors();
    }
}
