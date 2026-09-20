using FluentValidation;

namespace BoardingHouse.Api.DTOs.Users;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Email.Value)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters")
            .When(x => x.Email.IsSet)
            .OverridePropertyName("Email");

        RuleFor(x => x.Phone.Value)
            .MaximumLength(20).WithMessage("Phone exceeds 20 characters")
            .Matches(@"^(\+?\d+)?$").WithMessage("Phone must contain only digits, optionally starting with +")
            .When(x => x.Phone is { IsSet: true, Value: not null })
            .OverridePropertyName("Phone");

        RuleFor(x => x.FullName.Value)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(255).WithMessage("Full name exceeds 255 characters")
            .When(x => x.FullName.IsSet)
            .OverridePropertyName("FullName");
    }
}
