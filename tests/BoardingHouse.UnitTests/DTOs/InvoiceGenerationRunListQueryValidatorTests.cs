using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Invoices;

namespace BoardingHouse.UnitTests.DTOs;

public class InvoiceGenerationRunListQueryValidatorTests
{
    private readonly InvoiceGenerationRunListQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyQuery_HasNoErrors()
    {
        _validator.TestValidate(new InvoiceGenerationRunListQuery()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BillingMonthFirstDay_HasNoErrors()
    {
        _validator.TestValidate(new InvoiceGenerationRunListQuery { BillingMonth = new DateOnly(2026, 6, 1) })
            .ShouldNotHaveAnyValidationErrors();
    }

    // Listing is read-only: any past or future month may be filtered on
    [Fact]
    public void Validate_BillingMonthFarAhead_HasNoErrors()
    {
        _validator.TestValidate(new InvoiceGenerationRunListQuery { BillingMonth = new DateOnly(2030, 1, 1) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BillingMonthNotFirstDay_HasError()
    {
        _validator.TestValidate(new InvoiceGenerationRunListQuery { BillingMonth = new DateOnly(2026, 6, 5) })
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth must be the first day of a month");
    }
}
