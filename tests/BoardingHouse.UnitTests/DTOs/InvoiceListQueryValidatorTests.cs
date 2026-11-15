using FluentValidation.TestHelper;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Invoices;

namespace BoardingHouse.UnitTests.DTOs;

public class InvoiceListQueryValidatorTests
{
    private readonly InvoiceListQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyQuery_HasNoErrors()
    {
        _validator.TestValidate(new InvoiceListQuery()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BillingMonthFirstDay_HasNoErrors()
    {
        _validator.TestValidate(new InvoiceListQuery { BillingMonth = new DateOnly(2026, 6, 1) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(30)]
    public void Validate_BillingMonthNotFirstDay_HasError(int day)
    {
        _validator.TestValidate(new InvoiceListQuery { BillingMonth = new DateOnly(2026, 6, day) })
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth must be the first day of a month");
    }

    [Fact]
    public void Default_SortOrder_IsDescending()
    {
        Assert.Equal(SortOrder.Desc, new InvoiceListQuery().SortOrder);
    }
}
