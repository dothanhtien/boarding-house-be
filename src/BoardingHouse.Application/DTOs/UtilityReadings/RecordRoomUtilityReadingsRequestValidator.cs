using FluentValidation;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public class RecordRoomUtilityReadingsRequestValidator : AbstractValidator<RecordRoomUtilityReadingsRequest>
{
    public RecordRoomUtilityReadingsRequestValidator()
    {
        RuleFor(x => x.ReadOn).NotInTheFuture();

        RuleFor(x => x.ReadingMonth).NotNull().WithMessage("ReadingMonth is required");
        RuleFor(x => x.ReadingMonth!.Value)
            .ValidReadingMonth()
            .OverridePropertyName(nameof(RecordRoomUtilityReadingsRequest.ReadingMonth))
            .When(x => x.ReadingMonth is not null);

        RuleFor(x => x.ReadOn ?? DateOnly.FromDateTime(DateTime.UtcNow))
            .Must((x, readOn) => readOn >= x.ReadingMonth!.Value.AddMonths(-1) && readOn < x.ReadingMonth.Value.AddMonths(1))
            .WithMessage("ReadOn must fall within ReadingMonth or the month before it")
            .OverridePropertyName(nameof(RecordRoomUtilityReadingsRequest.ReadOn))
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
