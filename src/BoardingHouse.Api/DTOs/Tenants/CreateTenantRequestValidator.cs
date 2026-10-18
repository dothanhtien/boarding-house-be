using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.TenantProfiles;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.Tenants;

public class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(255).WithMessage("Full name exceeds 255 characters");
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required")
            .Must(PhoneNumber.IsValid).WithMessage("Phone is invalid");
        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(72).WithMessage("Password exceeds 72 characters");
        RuleFor(x => x.PasswordConfirmation)
            .NotEmpty().WithMessage("Password confirmation is required")
            .Equal(x => x.Password).WithMessage("Password confirmation does not match password");
        RuleFor(x => x.Profile!)
            .SetValidator(new UpdateTenantProfileRequestValidator())
            .When(x => x.Profile is not null);
    }
}
