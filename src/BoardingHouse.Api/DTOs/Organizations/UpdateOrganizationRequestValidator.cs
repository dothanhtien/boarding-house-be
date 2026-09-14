using FluentValidation;

namespace BoardingHouse.Api.DTOs.Organizations;

public class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.TaxCode)
            .MaximumLength(20).WithMessage("Tax code exceeds 20 characters")
            .When(x => x.TaxCode is not null);
        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone exceeds 20 characters")
            .Matches(@"^(\+?\d+)?$").WithMessage("Phone must contain only digits, optionally starting with +")
            .When(x => x.Phone is not null);
        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters")
            .When(x => x.Email is not null);
        RuleFor(x => x.Province)
            .MaximumLength(100).WithMessage("Province exceeds 100 characters")
            .When(x => x.Province is not null);
        RuleFor(x => x.District)
            .MaximumLength(100).WithMessage("District exceeds 100 characters")
            .When(x => x.District is not null);
        RuleFor(x => x.Ward)
            .MaximumLength(100).WithMessage("Ward exceeds 100 characters")
            .When(x => x.Ward is not null);
    }
}
