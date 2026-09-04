using FluentValidation;

namespace BoardingHouse.Api.DTOs.Auth;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters");
        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone exceeds 20 characters")
            .When(x => x.Phone is not null);
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(255).WithMessage("Full name exceeds 255 characters");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(72).WithMessage("Password exceeds 72 characters");
        RuleFor(x => x.PasswordConfirmation)
            .NotEmpty().WithMessage("Password confirmation is required")
            .Equal(x => x.Password).WithMessage("Password confirmation does not match password");
    }
}
