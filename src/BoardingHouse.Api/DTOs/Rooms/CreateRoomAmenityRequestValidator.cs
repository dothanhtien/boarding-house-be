using FluentValidation;

namespace BoardingHouse.Api.DTOs.Rooms;

public class CreateRoomAmenityRequestValidator : AbstractValidator<CreateRoomAmenityRequest>
{
    public CreateRoomAmenityRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1")
            .When(x => x.Quantity is not null);

        RuleFor(x => x.Icon)
            .MaximumLength(50).WithMessage("Icon must not exceed 50 characters")
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$").WithMessage("Icon must be a kebab-case key (e.g. \"air-conditioner\")")
            .When(x => x.Icon is not null);
    }
}
