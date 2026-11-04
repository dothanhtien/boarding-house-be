using BoardingHouse.Domain.Common;
using FluentValidation;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public class UtilityReadingRoomListQueryValidator : AbstractValidator<UtilityReadingRoomListQuery>
{
    public UtilityReadingRoomListQueryValidator()
    {
        RuleFor(x => x.ReadingMonth)
            .NotNull().WithMessage("ReadingMonth is required")
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("ReadingMonth must be the first day of a month");
    }
}
