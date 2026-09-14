using FluentValidation;

namespace BoardingHouse.Api.DTOs.Users;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Email is not null || x.Phone is not null || x.FullName is not null || x.IsActive is not null)
            .WithMessage("At least one field must be provided")
            .WithName("Request");
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters")
            .When(x => x.Email is not null);
        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone exceeds 20 characters")
            .Matches(@"^(\+?\d+)?$").WithMessage("Phone must contain only digits, optionally starting with +")
            .When(x => x.Phone is not null);
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(255).WithMessage("Full name exceeds 255 characters")
            .When(x => x.FullName is not null);
    }
}
