using FluentValidation;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Invoices;

public class UpdateInvoiceRequestValidator : AbstractValidator<UpdateInvoiceRequest>
{
    public UpdateInvoiceRequestValidator()
    {
        RuleFor(x => x.DiscountAmount.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DiscountAmount must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DiscountAmount must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DiscountAmount.IsSet)
            .OverridePropertyName("DiscountAmount");

        RuleFor(x => x.DiscountReason.Value)
            .MaximumLength(Invoice.DiscountReasonMaxLength)
            .WithMessage($"DiscountReason must not exceed {Invoice.DiscountReasonMaxLength} characters")
            .When(x => x.DiscountReason is { IsSet: true, Value: not null })
            .OverridePropertyName("DiscountReason");

        RuleFor(x => x.Notes.Value)
            .MaximumLength(Invoice.NotesMaxLength)
            .WithMessage($"Notes must not exceed {Invoice.NotesMaxLength} characters")
            .When(x => x.Notes is { IsSet: true, Value: not null })
            .OverridePropertyName("Notes");

        RuleFor(x => x.CustomItems.Value)
            .NotNull().WithMessage("CustomItems must not be null — omit the field to leave custom items unchanged")
            .Must(items => items is null || items.Count <= Invoice.MaxCustomItems)
            .WithMessage($"CustomItems must not contain more than {Invoice.MaxCustomItems} changes per request")
            .Must(items => items is null || items.Where(i => i?.Id is not null).Select(i => i!.Id).AllUnique())
            .WithMessage("Each item Id must appear at most once")
            .When(x => x.CustomItems.IsSet)
            .OverridePropertyName("CustomItems");

        RuleForEach(x => x.CustomItems.Value)
            .NotNull().WithMessage("Item must not be null")
            .SetValidator(new UpdateInvoiceCustomItemRequestValidator())
            .When(x => x.CustomItems is { IsSet: true, Value: not null })
            .OverridePropertyName("CustomItems");
    }
}
