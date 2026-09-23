using FluentValidation;

namespace BoardingHouse.Api.DTOs.Properties;

public class UpdatePropertyRequestValidator : AbstractValidator<UpdatePropertyRequest>
{
    public UpdatePropertyRequestValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters")
            .When(x => x.Name.IsSet)
            .OverridePropertyName("Name");

        RuleFor(x => x.Province.Value)
            .MaximumLength(100).WithMessage("Province exceeds 100 characters")
            .When(x => x.Province is { IsSet: true, Value: not null })
            .OverridePropertyName("Province");

        RuleFor(x => x.District.Value)
            .MaximumLength(100).WithMessage("District exceeds 100 characters")
            .When(x => x.District is { IsSet: true, Value: not null })
            .OverridePropertyName("District");

        RuleFor(x => x.Ward.Value)
            .MaximumLength(100).WithMessage("Ward exceeds 100 characters")
            .When(x => x.Ward is { IsSet: true, Value: not null })
            .OverridePropertyName("Ward");

        RuleFor(x => x.DefaultBillingDay.Value)
            .InclusiveBetween(1, 28).WithMessage("Default billing day must be between 1 and 28")
            .When(x => x.DefaultBillingDay is { IsSet: true, Value: not null })
            .OverridePropertyName("DefaultBillingDay");

        RuleFor(x => x)
            .Must(x =>
            {
                if (!x.LateFeeType.IsSet && !x.LateFeeValue.IsSet)
                {
                    return true;
                }

                return x.LateFeeType.IsSet && x.LateFeeValue.IsSet
                    && (x.LateFeeType.Value is null) == (x.LateFeeValue.Value is null);
            })
            .WithMessage("Late fee type and Late fee value must be set together")
            .WithName(nameof(UpdatePropertyRequest.LateFeeValue));

        RuleFor(x => x.LateFeeType.Value)
            .IsInEnum().WithMessage("Late fee type is invalid")
            .When(x => x.LateFeeType is { IsSet: true, Value: not null })
            .OverridePropertyName("LateFeeType");

        RuleFor(x => x.LateFeeGraceDays.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Late fee grace days must not be negative")
            .When(x => x.LateFeeGraceDays is { IsSet: true, Value: not null })
            .OverridePropertyName("LateFeeGraceDays");
    }
}
