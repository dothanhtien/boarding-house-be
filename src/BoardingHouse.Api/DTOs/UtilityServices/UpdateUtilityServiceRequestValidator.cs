using FluentValidation;

namespace BoardingHouse.Api.DTOs.UtilityServices;

public class UpdateUtilityServiceRequestValidator : AbstractValidator<UpdateUtilityServiceRequest>
{
    public UpdateUtilityServiceRequestValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters")
            .When(x => x.Name.IsSet)
            .OverridePropertyName("Name");

        RuleFor(x => x.Unit.Value)
            .NotEmpty().WithMessage("Unit is required")
            .MaximumLength(30).WithMessage("Unit must not exceed 30 characters")
            .When(x => x.Unit.IsSet)
            .OverridePropertyName("Unit");

        RuleFor(x => x.DefaultUnitPrice.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DefaultUnitPrice must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DefaultUnitPrice must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DefaultUnitPrice is { IsSet: true, Value: not null })
            .OverridePropertyName("DefaultUnitPrice");
    }
}
