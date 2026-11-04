using FluentValidation;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public class RecordRoomUtilityReadingsRequestValidator : AbstractValidator<RecordRoomUtilityReadingsRequest>
{
    public RecordRoomUtilityReadingsRequestValidator()
    {
        RuleFor(x => x.ReadOn)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("ReadOn must not be in the future");

        RuleFor(x => x.ReadingMonth)
            .NotNull().WithMessage("ReadingMonth is required")
            .Must(month => BillingMonth.IsFirstDay(month!.Value)).WithMessage("ReadingMonth must be the first day of a month")
            .LessThanOrEqualTo(_ => BillingMonth.Of(DateOnly.FromDateTime(DateTime.UtcNow)).AddMonths(1))
            .WithMessage("ReadingMonth must not be later than next month");

        RuleFor(x => x.ReadOn)
            .Must((x, readOn) => BillingMonth.MonthsBetween(
                readOn ?? DateOnly.FromDateTime(DateTime.UtcNow), x.ReadingMonth!.Value) is 0 or 1)
            .WithMessage("ReadOn must fall within ReadingMonth or the month before it")
            .When(x => x.ReadingMonth is { } month && BillingMonth.IsFirstDay(month));

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Items must not be empty")
            .Must(items => items.Count <= RecordRoomUtilityReadingsRequest.MaxItems)
            .WithMessage($"At most {RecordRoomUtilityReadingsRequest.MaxItems} readings can be recorded per request")
            .Must(items => items.Where(i => i is not null).Select(i => i.ServiceId).AllUnique())
            .WithMessage("Each utility service can only appear once");

        RuleForEach(x => x.Items)
            .NotNull().WithMessage("Item must not be null")
            .SetValidator(new RecordRoomUtilityReadingItemRequestValidator());
    }
}
