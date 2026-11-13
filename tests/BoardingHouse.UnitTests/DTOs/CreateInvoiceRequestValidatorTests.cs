using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateInvoiceRequestValidatorTests
{
    private readonly CreateInvoiceRequestValidator _validator = new();

    private static DateOnly ThisMonth => BillingMonth.Of(DateOnly.FromDateTime(DateTime.UtcNow)).FirstDay;

    private static CreateInvoiceRequest Request(
        List<CreateInvoiceCustomItemRequest>? customItems = null,
        decimal discountAmount = 0,
        string? discountReason = null) => new()
    {
        LeaseId = Guid.NewGuid(),
        BillingMonth = ThisMonth,
        CustomItems = customItems,
        DiscountAmount = discountAmount,
        DiscountReason = discountReason
    };

    private static CreateInvoiceCustomItemRequest Item(
        string? description = "Repair", decimal quantity = 1, decimal? unitPrice = 100_000) => new()
    {
        Description = description,
        Quantity = quantity,
        UnitPrice = unitPrice
    };

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        _validator.TestValidate(Request([Item()], 50_000, "Loyal tenant")).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithoutLeaseId_HasError()
    {
        _validator.TestValidate(Request() with { LeaseId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.LeaseId);
    }

    [Fact]
    public void Validate_WithoutBillingMonth_HasError()
    {
        _validator.TestValidate(Request() with { BillingMonth = null }).ShouldHaveValidationErrorFor(x => x.BillingMonth);
    }

    [Fact]
    public void Validate_BillingMonthNotFirstDay_HasError()
    {
        _validator.TestValidate(Request() with { BillingMonth = ThisMonth.AddDays(1) })
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth must be the first day of a month");
    }

    [Fact]
    public void Validate_BillingMonthNextMonth_HasNoErrors()
    {
        _validator.TestValidate(Request() with { BillingMonth = ThisMonth.AddMonths(1) }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BillingMonthAfterNextMonth_HasError()
    {
        _validator.TestValidate(Request() with { BillingMonth = ThisMonth.AddMonths(2) })
            .ShouldHaveValidationErrorFor(x => x.BillingMonth)
            .WithErrorMessage("BillingMonth must not be later than next month");
    }

    [Fact]
    public void Validate_NegativeDiscount_HasError()
    {
        _validator.TestValidate(Request(discountAmount: -1, discountReason: "Reason"))
            .ShouldHaveValidationErrorFor(x => x.DiscountAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_DiscountWithoutReason_HasError(string? reason)
    {
        _validator.TestValidate(Request(discountAmount: 50_000, discountReason: reason))
            .ShouldHaveValidationErrorFor(x => x.DiscountReason);
    }

    [Fact]
    public void Validate_TooManyCustomItems_HasError()
    {
        var items = Enumerable.Range(0, 21).Select(_ => Item()).ToList();

        _validator.TestValidate(Request(items))
            .ShouldHaveValidationErrorFor(x => x.CustomItems)
            .WithErrorMessage("At most 20 custom items are allowed");
    }

    [Fact]
    public void Validate_CustomItemWithZeroQuantity_HasError()
    {
        _validator.TestValidate(Request([Item(quantity: 0)])).ShouldHaveValidationErrorFor("CustomItems[0].Quantity");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Validate_CustomItemWithoutUnitPrice_HasError(int? unitPrice)
    {
        _validator.TestValidate(Request([Item(unitPrice: unitPrice)])).ShouldHaveValidationErrorFor("CustomItems[0].UnitPrice");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_CustomItemWithoutDescription_HasError(string description)
    {
        _validator.TestValidate(Request([Item(description)])).ShouldHaveValidationErrorFor("CustomItems[0].Description");
    }

    [Fact]
    public void Validate_CustomItemDescriptionTooLong_HasError()
    {
        _validator.TestValidate(Request([Item(new string('a', 256))]))
            .ShouldHaveValidationErrorFor("CustomItems[0].Description")
            .WithErrorMessage("Description must not exceed 255 characters");
    }

    [Fact]
    public void Validate_CustomItemWithNegativeUnitPrice_HasNoErrors()
    {
        _validator.TestValidate(Request([Item("Refund", unitPrice: -100_000)])).ShouldNotHaveAnyValidationErrors();
    }
}
