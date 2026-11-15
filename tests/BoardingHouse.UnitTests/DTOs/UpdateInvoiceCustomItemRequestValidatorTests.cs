using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateInvoiceCustomItemRequestValidatorTests
{
    private readonly UpdateInvoiceCustomItemRequestValidator _validator = new();

    private static UpdateInvoiceCustomItemRequest NewItem(
        string? description = "Repair", decimal? quantity = 1, decimal? unitPrice = 100_000) => new()
    {
        Description = description,
        Quantity = quantity,
        UnitPrice = unitPrice
    };

    private static UpdateInvoiceCustomItemRequest Existing() => new() { Id = Guid.NewGuid() };

    [Fact]
    public void Validate_NewItem_HasNoErrors()
    {
        _validator.TestValidate(NewItem()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NewNegativeItem_HasNoErrors()
    {
        _validator.TestValidate(NewItem(unitPrice: -50_000)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NewItemWithoutDescription_HasError()
    {
        _validator.TestValidate(NewItem(description: null))
            .ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Description is required");
    }

    [Fact]
    public void Validate_NewItemWithoutQuantity_HasError()
    {
        _validator.TestValidate(NewItem(quantity: null))
            .ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity is required");
    }

    [Fact]
    public void Validate_NewItemWithoutUnitPrice_HasError()
    {
        _validator.TestValidate(NewItem(unitPrice: null))
            .ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("UnitPrice is required");
    }

    [Fact]
    public void Validate_NewItemDeleted_HasError()
    {
        _validator.TestValidate(NewItem() with { IsDeleted = true })
            .ShouldHaveValidationErrorFor(x => x.IsDeleted)
            .WithErrorMessage("IsDeleted requires Id — only existing items can be deleted");
    }

    [Fact]
    public void Validate_ExistingItemWithoutFields_HasNoErrors()
    {
        _validator.TestValidate(Existing()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ExistingItemDeleted_HasNoErrors()
    {
        _validator.TestValidate(Existing() with { IsDeleted = true }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ExistingItemPartialChange_HasNoErrors()
    {
        _validator.TestValidate(Existing() with { Quantity = 2.5m }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankDescription_HasError(string description)
    {
        _validator.TestValidate(Existing() with { Description = description })
            .ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Description is required");
    }

    [Fact]
    public void Validate_DescriptionTooLong_HasError()
    {
        _validator.TestValidate(NewItem(description: new string('a', InvoiceItem.DescriptionMaxLength + 1)))
            .ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage($"Description must not exceed {InvoiceItem.DescriptionMaxLength} characters");
    }

    [Fact]
    public void Validate_DescriptionAtMaxLengthAfterTrim_HasNoErrors()
    {
        _validator.TestValidate(NewItem(description: $"  {new string('a', InvoiceItem.DescriptionMaxLength)}  "))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_QuantityNotPositive_HasError(int quantity)
    {
        _validator.TestValidate(Existing() with { Quantity = quantity })
            .ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity must be greater than 0");
    }

    [Fact]
    public void Validate_QuantityWithTooManyDecimals_HasError()
    {
        _validator.TestValidate(Existing() with { Quantity = 1.234m }).ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Validate_UnitPriceZero_HasError()
    {
        _validator.TestValidate(Existing() with { UnitPrice = 0 })
            .ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("UnitPrice must not be 0");
    }

    [Fact]
    public void Validate_UnitPriceWithTooManyDecimals_HasError()
    {
        _validator.TestValidate(Existing() with { UnitPrice = 1.234m }).ShouldHaveValidationErrorFor(x => x.UnitPrice);
    }
}
