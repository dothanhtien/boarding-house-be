using FluentValidation;

namespace BoardingHouse.Api.DTOs.Users;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone exceeds 20 characters")
            .When(x => x.Phone is not null);
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(255).WithMessage("Full name exceeds 255 characters");
    }
}
