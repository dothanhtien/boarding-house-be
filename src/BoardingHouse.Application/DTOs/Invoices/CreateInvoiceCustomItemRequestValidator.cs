using FluentValidation;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Invoices;

public class CreateInvoiceCustomItemRequestValidator : AbstractValidator<CreateInvoiceCustomItemRequest>
{
    public CreateInvoiceCustomItemRequestValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .Must(d => d!.Trim().Length <= InvoiceItem.DescriptionMaxLength)
            .WithMessage($"Description must not exceed {InvoiceItem.DescriptionMaxLength} characters");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Quantity must not exceed 18 digits in total, with at most 2 decimal places");

        RuleFor(x => x.UnitPrice)
            .NotNull().WithMessage("UnitPrice is required")
            .NotEqual(0).WithMessage("UnitPrice must not be 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("UnitPrice must not exceed 18 digits in total, with at most 2 decimal places");
    }
}
