using FluentValidation;

namespace BoardingHouse.Api.DTOs.Organizations;

public class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(255).WithMessage("Name must not exceed 255 characters")
            .When(x => x.Name.IsSet)
            .OverridePropertyName("Name");

        RuleFor(x => x.OwnerId)
            .NotEqual(Guid.Empty).WithMessage("Owner Id is required")
            .When(x => x.OwnerId is not null);

        RuleFor(x => x.TaxCode.Value)
            .MaximumLength(20).WithMessage("Tax code exceeds 20 characters")
            .When(x => x.TaxCode is { IsSet: true, Value: not null })
            .OverridePropertyName("TaxCode");

        RuleFor(x => x.Phone.Value)
            .MaximumLength(20).WithMessage("Phone exceeds 20 characters")
            .Matches(@"^(\+?\d+)?$").WithMessage("Phone must contain only digits, optionally starting with +")
            .When(x => x.Phone is { IsSet: true, Value: not null })
            .OverridePropertyName("Phone");

        RuleFor(x => x.Email.Value)
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters")
            .When(x => x.Email is { IsSet: true, Value: not null })
            .OverridePropertyName("Email");

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
    }
}
