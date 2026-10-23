using FluentValidation;

namespace BoardingHouse.Application.DTOs.Leases;

public class UpsertLeaseUtilityServiceRequestValidator : AbstractValidator<UpsertLeaseUtilityServiceRequest>
{
    public UpsertLeaseUtilityServiceRequestValidator()
    {
        RuleFor(x => x.UnitPriceOverride.Value)
            .GreaterThanOrEqualTo(0).WithMessage("UnitPriceOverride must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("UnitPriceOverride must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.UnitPriceOverride is { IsSet: true, Value: not null })
            .OverridePropertyName("UnitPriceOverride");
    }
}
