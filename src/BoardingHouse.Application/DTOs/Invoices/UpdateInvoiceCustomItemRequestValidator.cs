using FluentValidation;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Invoices;

public class UpdateInvoiceCustomItemRequestValidator : AbstractValidator<UpdateInvoiceCustomItemRequest>
{
    public UpdateInvoiceCustomItemRequestValidator()
    {
        RuleFor(x => x.IsDeleted)
            .Equal(false).WithMessage("IsDeleted requires Id — only existing items can be deleted")
            .When(x => x.Id is null);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .Must(d => d!.Trim().Length <= InvoiceItem.DescriptionMaxLength)
            .WithMessage($"Description must not exceed {InvoiceItem.DescriptionMaxLength} characters")
            .When(x => x.Id is null || x.Description is not null);

        RuleFor(x => x.Quantity)
            .NotNull().WithMessage("Quantity is required")
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Quantity must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.Id is null || x.Quantity is not null);

        RuleFor(x => x.UnitPrice)
            .NotNull().WithMessage("UnitPrice is required")
            .NotEqual(0).WithMessage("UnitPrice must not be 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("UnitPrice must not exceed 18 digits in total, with at most 2 decimal places")
            .Must((x, unitPrice) => x.Quantity is not > 0
                || InvoiceItem.IsCustomAmountInRange(x.Quantity.Value, unitPrice!.Value))
            .WithMessage(InvoiceItem.AmountOutOfRangeMessage)
            .When(x => x.Id is null || x.UnitPrice is not null);
    }
}
