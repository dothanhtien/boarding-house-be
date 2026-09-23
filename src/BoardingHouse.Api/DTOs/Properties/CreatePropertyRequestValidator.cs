using FluentValidation;

namespace BoardingHouse.Api.DTOs.Properties;

public class CreatePropertyRequestValidator : AbstractValidator<CreatePropertyRequest>
{
    public CreatePropertyRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.Province).MaximumLength(100).WithMessage("Province exceeds 100 characters")
            .When(x => x.Province is not null);

        RuleFor(x => x.District).MaximumLength(100).WithMessage("District exceeds 100 characters")
            .When(x => x.District is not null);

        RuleFor(x => x.Ward).MaximumLength(100).WithMessage("Ward exceeds 100 characters")
            .When(x => x.Ward is not null);

        RuleFor(x => x.DefaultBillingDay)
            .InclusiveBetween(1, 28).WithMessage("DefaultBillingDay must be between 1 and 28")
            .When(x => x.DefaultBillingDay is not null);

        RuleFor(x => x)
            .Must(x => (x.LateFeeType is null) == (x.LateFeeValue is null))
            .WithMessage("LateFeeType and LateFeeValue must be set together")
            .WithName(nameof(CreatePropertyRequest.LateFeeValue));

        RuleFor(x => x.LateFeeType)
            .IsInEnum().WithMessage("Late fee type is invalid")
            .When(x => x.LateFeeType is not null);

        RuleFor(x => x.LateFeeGraceDays)
            .GreaterThanOrEqualTo(0).WithMessage("LateFeeGraceDays must not be negative")
            .When(x => x.LateFeeGraceDays is not null);
    }
}
