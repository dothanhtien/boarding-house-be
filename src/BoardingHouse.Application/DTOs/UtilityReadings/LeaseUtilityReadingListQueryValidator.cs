using FluentValidation;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public class LeaseUtilityReadingListQueryValidator : AbstractValidator<LeaseUtilityReadingListQuery>
{
    public LeaseUtilityReadingListQueryValidator()
    {
        RuleFor(x => x.From)
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("From must be the first day of a month")
            .When(x => x.From is not null);

        RuleFor(x => x.To)
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("To must be the first day of a month")
            .Must((query, to) => query.From is null || to >= query.From).WithMessage("To must not be before From")
            .When(x => x.To is not null);
    }
}
