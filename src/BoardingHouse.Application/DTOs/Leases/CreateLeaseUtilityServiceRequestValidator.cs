using FluentValidation;

namespace BoardingHouse.Application.DTOs.Leases;

public class CreateLeaseUtilityServiceRequestValidator : AbstractValidator<CreateLeaseUtilityServiceRequest>
{
    public CreateLeaseUtilityServiceRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty().WithMessage("ServiceId is required");

        RuleFor(x => x.UnitPriceOverride)
            .GreaterThanOrEqualTo(0).WithMessage("UnitPriceOverride must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("UnitPriceOverride must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.UnitPriceOverride is not null);

        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0");
    }
}
