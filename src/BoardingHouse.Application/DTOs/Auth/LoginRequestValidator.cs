using BoardingHouse.Application.Common;
using FluentValidation;

namespace BoardingHouse.Application.DTOs.Auth;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Email or phone is required");

        RuleFor(x => x.Identifier)
            .EmailAddress().WithMessage("Email is invalid")
            .When(x => !string.IsNullOrWhiteSpace(x.Identifier) && LoginRequest.IsEmail(x.Identifier));

        RuleFor(x => x.Identifier)
            .Must(PhoneNumber.IsValid).WithMessage("Phone is invalid")
            .When(x => !string.IsNullOrWhiteSpace(x.Identifier) && !LoginRequest.IsEmail(x.Identifier));

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}
