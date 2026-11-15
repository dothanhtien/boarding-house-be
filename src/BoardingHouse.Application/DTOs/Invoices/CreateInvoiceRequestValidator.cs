using FluentValidation;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Invoices;

public class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.LeaseId).NotEmpty().WithMessage("LeaseId is required");

        RuleFor(x => x.BillingMonth)
            .NotNull().WithMessage("BillingMonth is required")
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("BillingMonth must be the first day of a month")
            .LessThanOrEqualTo(_ => BillingMonth.Of(DateOnly.FromDateTime(DateTime.UtcNow)).AddMonths(1).FirstDay)
            .WithMessage("BillingMonth must not be later than next month");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage("DiscountAmount must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DiscountAmount must not exceed 18 digits in total, with at most 2 decimal places");

        RuleFor(x => x.DiscountReason)
            .NotEmpty().WithMessage("DiscountReason is required when DiscountAmount is greater than 0")
            .When(x => x.DiscountAmount > 0);

        RuleFor(x => x.DiscountReason)
            .MaximumLength(Invoice.DiscountReasonMaxLength)
            .WithMessage($"DiscountReason must not exceed {Invoice.DiscountReasonMaxLength} characters");

        RuleFor(x => x.Notes)
            .MaximumLength(Invoice.NotesMaxLength)
            .WithMessage($"Notes must not exceed {Invoice.NotesMaxLength} characters");

        RuleFor(x => x.CustomItems)
            .Must(items => items!.Count <= Invoice.MaxCustomItems)
            .WithMessage($"At most {Invoice.MaxCustomItems} custom items are allowed")
            .When(x => x.CustomItems is not null);

        RuleForEach(x => x.CustomItems)
            .NotNull().WithMessage("Item must not be null")
            .SetValidator(new CreateInvoiceCustomItemRequestValidator());
    }
}
