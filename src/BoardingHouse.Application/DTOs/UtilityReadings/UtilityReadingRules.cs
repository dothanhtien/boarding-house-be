using BoardingHouse.Domain.Common;
using FluentValidation;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public static class UtilityReadingRules
{
    public static IRuleBuilderOptions<T, DateOnly> ValidReadingMonth<T>(this IRuleBuilder<T, DateOnly> rule) =>
        rule
            .Must(BillingMonth.IsFirstDay).WithMessage("ReadingMonth must be the first day of a month")
            .LessThanOrEqualTo(_ => BillingMonth.Of(DateOnly.FromDateTime(DateTime.UtcNow)).AddMonths(1))
            .WithMessage("ReadingMonth must not be later than next month");

    public static IRuleBuilderOptions<T, DateOnly?> NotInTheFuture<T>(this IRuleBuilder<T, DateOnly?> rule) =>
        rule
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("ReadOn must not be in the future");
}
