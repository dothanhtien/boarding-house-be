using FluentValidation;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.DTOs.Invoices;

public class GenerateInvoicesRequestValidator : AbstractValidator<GenerateInvoicesRequest>
{
    public GenerateInvoicesRequestValidator()
    {
        RuleFor(x => x.BillingMonth)
            .NotNull().WithMessage("BillingMonth is required")
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("BillingMonth must be the first day of a month")
            .LessThanOrEqualTo(_ => BillingMonth.Of(DateOnly.FromDateTime(DateTime.UtcNow)).AddMonths(1).FirstDay)
            .WithMessage("BillingMonth must not be later than next month");
    }
}
