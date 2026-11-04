using FluentValidation;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public class RecordRoomUtilityReadingItemRequestValidator : AbstractValidator<RecordRoomUtilityReadingItemRequest>
{
    public RecordRoomUtilityReadingItemRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty().WithMessage("ServiceId is required");

        RuleFor(x => x.CurrentValue)
            .GreaterThanOrEqualTo(0).WithMessage("CurrentValue must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("CurrentValue must not exceed 18 digits in total, with at most 2 decimal places");

        RuleFor(x => x.PreviousValue)
            .GreaterThanOrEqualTo(0).WithMessage("PreviousValue must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("PreviousValue must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.PreviousValue is not null);
    }
}
