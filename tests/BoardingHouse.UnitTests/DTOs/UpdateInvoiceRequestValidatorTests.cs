using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateInvoiceRequestValidatorTests
{
    private readonly UpdateInvoiceRequestValidator _validator = new();

    private static UpdateInvoiceCustomItemRequest NewItem(
        string? description = "Repair", decimal? quantity = 1, decimal? unitPrice = 100_000) => new()
    {
        Description = description,
        Quantity = quantity,
        UnitPrice = unitPrice
    };

    [Fact]
    public void Validate_EmptyRequest_IsValid()
    {
        _validator.TestValidate(new UpdateInvoiceRequest()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EveryFieldValid_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateInvoiceRequest
        {
            DiscountAmount = 50_000m,
            DiscountReason = "Loyal tenant",
            Notes = "Note",
            DueDate = new DateOnly(2026, 6, 15),
            CustomItems = new List<UpdateInvoiceCustomItemRequest>
            {
                NewItem(),
                new() { Id = Guid.NewGuid(), Quantity = 2 },
                new() { Id = Guid.NewGuid(), IsDeleted = true }
            }
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DiscountAmountZero_HasNoErrors()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { DiscountAmount = 0m }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DiscountAmountWithoutReason_IsLeftToTheDomain()
    {
        // The reason may already be on the invoice: Invoice.Update checks the merged state
        _validator.TestValidate(new UpdateInvoiceRequest { DiscountAmount = 50_000m }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NegativeDiscountAmount_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { DiscountAmount = -1m })
            .ShouldHaveValidationErrorFor("DiscountAmount")
            .WithErrorMessage("DiscountAmount must not be negative");
    }

    [Fact]
    public void Validate_DiscountAmountWithTooManyDecimals_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { DiscountAmount = 1.234m })
            .ShouldHaveValidationErrorFor("DiscountAmount");
    }

    [Fact]
    public void Validate_DiscountReasonNull_HasNoErrors()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { DiscountReason = null }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DiscountReasonTooLong_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { DiscountReason = new string('a', Invoice.DiscountReasonMaxLength + 1) })
            .ShouldHaveValidationErrorFor("DiscountReason");
    }

    [Fact]
    public void Validate_NotesNull_HasNoErrors()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { Notes = null }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NotesTooLong_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { Notes = new string('a', Invoice.NotesMaxLength + 1) })
            .ShouldHaveValidationErrorFor("Notes");
    }

    [Fact]
    public void Validate_CustomItemsNull_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { CustomItems = null })
            .ShouldHaveValidationErrorFor("CustomItems")
            .WithErrorMessage("CustomItems must not be null — omit the field to leave custom items unchanged");
    }

    [Fact]
    public void Validate_CustomItemsEmpty_HasNoErrors()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { CustomItems = new List<UpdateInvoiceCustomItemRequest>() })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_TooManyCustomItemChanges_HasError()
    {
        var items = Enumerable.Range(0, Invoice.MaxCustomItems + 1).Select(_ => NewItem()).ToList();

        _validator.TestValidate(new UpdateInvoiceRequest { CustomItems = items })
            .ShouldHaveValidationErrorFor("CustomItems")
            .WithErrorMessage($"CustomItems must not contain more than {Invoice.MaxCustomItems} changes per request");
    }

    [Fact]
    public void Validate_CustomItemChangesAtTheLimit_HasNoErrors()
    {
        var items = Enumerable.Range(0, Invoice.MaxCustomItems).Select(_ => NewItem()).ToList();

        _validator.TestValidate(new UpdateInvoiceRequest { CustomItems = items }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DuplicateItemId_HasError()
    {
        var id = Guid.NewGuid();

        _validator.TestValidate(new UpdateInvoiceRequest
            {
                CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = id, Quantity = 2 }, new() { Id = id, IsDeleted = true } }
            })
            .ShouldHaveValidationErrorFor("CustomItems")
            .WithErrorMessage("Each item Id must appear at most once");
    }

    [Fact]
    public void Validate_SeveralNewItems_AreNotDuplicates()
    {
        _validator.TestValidate(new UpdateInvoiceRequest
            {
                CustomItems = new List<UpdateInvoiceCustomItemRequest> { NewItem(), NewItem() }
            })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullItem_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest { CustomItems = new List<UpdateInvoiceCustomItemRequest> { null! } })
            .ShouldHaveValidationErrorFor("CustomItems[0]")
            .WithErrorMessage("Item must not be null");
    }

    [Fact]
    public void Validate_NewItemMissingFields_HasErrorsOnTheItem()
    {
        var result = _validator.TestValidate(new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { NewItem(), NewItem(null, null, null) }
        });

        result.ShouldHaveValidationErrorFor("CustomItems[1].Description");
        result.ShouldHaveValidationErrorFor("CustomItems[1].Quantity");
        result.ShouldHaveValidationErrorFor("CustomItems[1].UnitPrice");
        result.ShouldNotHaveValidationErrorFor("CustomItems[0].Description");
    }

    [Fact]
    public void Validate_NewItemDeleted_HasError()
    {
        _validator.TestValidate(new UpdateInvoiceRequest
            {
                CustomItems = new List<UpdateInvoiceCustomItemRequest> { NewItem() with { IsDeleted = true } }
            })
            .ShouldHaveValidationErrorFor("CustomItems[0].IsDeleted");
    }
}
