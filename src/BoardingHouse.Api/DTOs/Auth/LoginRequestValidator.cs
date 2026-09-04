using FluentValidation;

namespace BoardingHouse.Api.DTOs.Auth;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}
