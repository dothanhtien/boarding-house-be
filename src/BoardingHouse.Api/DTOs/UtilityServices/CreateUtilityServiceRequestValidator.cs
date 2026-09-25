using FluentValidation;

namespace BoardingHouse.Api.DTOs.UtilityServices;

public class CreateUtilityServiceRequestValidator : AbstractValidator<CreateUtilityServiceRequest>
{
    public CreateUtilityServiceRequestValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty().WithMessage("PropertyId is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.Type).IsInEnum().WithMessage("Type is invalid");

        RuleFor(x => x.Unit)
            .NotEmpty().WithMessage("Unit is required")
            .MaximumLength(30).WithMessage("Unit must not exceed 30 characters");

        RuleFor(x => x.DefaultUnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("DefaultUnitPrice must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DefaultUnitPrice must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DefaultUnitPrice is not null);
    }
}
