using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.UnitTests.DTOs;

public class GenerateInvoicesRequestValidatorTests
{
    private static readonly DateOnly CurrentMonth = BillingMonth.Of(DateOnly.FromDateTime(DateTime.UtcNow)).FirstDay;

    private readonly GenerateInvoicesRequestValidator _validator = new();

    [Fact]
    public void Validate_CurrentMonth_HasNoErrors()
    {
        _validator.TestValidate(new GenerateInvoicesRequest { BillingMonth = CurrentMonth }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NextMonth_HasNoErrors()
    {
        _validator.TestValidate(new GenerateInvoicesRequest { BillingMonth = CurrentMonth.AddMonths(1) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_PastMonth_HasNoErrors()
    {
        _validator.TestValidate(new GenerateInvoicesRequest { BillingMonth = CurrentMonth.AddMonths(-6) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MissingBillingMonth_HasError()
    {
        _validator.TestValidate(new GenerateInvoicesRequest())
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth is required");
    }

    [Fact]
    public void Validate_BillingMonthNotFirstDay_HasError()
    {
        _validator.TestValidate(new GenerateInvoicesRequest { BillingMonth = CurrentMonth.AddDays(4) })
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth must be the first day of a month");
    }

    [Fact]
    public void Validate_TwoMonthsAhead_HasError()
    {
        _validator.TestValidate(new GenerateInvoicesRequest { BillingMonth = CurrentMonth.AddMonths(2) })
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth must not be later than next month");
    }
}
