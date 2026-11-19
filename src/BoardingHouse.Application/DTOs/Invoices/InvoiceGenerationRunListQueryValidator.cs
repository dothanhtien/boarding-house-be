using FluentValidation;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.DTOs.Invoices;

public class InvoiceGenerationRunListQueryValidator : AbstractValidator<InvoiceGenerationRunListQuery>
{
    public InvoiceGenerationRunListQueryValidator()
    {
        RuleFor(x => x.BillingMonth)
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("BillingMonth must be the first day of a month")
            .When(x => x.BillingMonth is not null);
    }
}
