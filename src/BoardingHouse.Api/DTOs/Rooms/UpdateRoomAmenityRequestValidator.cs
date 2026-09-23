using FluentValidation;

namespace BoardingHouse.Api.DTOs.Rooms;

public class UpdateRoomAmenityRequestValidator : AbstractValidator<UpdateRoomAmenityRequest>
{
    public UpdateRoomAmenityRequestValidator()
    {
        RuleFor(x => x.IsDeleted)
            .Equal(false).WithMessage("IsDeleted requires Id — only existing amenities can be deleted")
            .When(x => x.Id is null);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required when adding an amenity")
            .When(x => x.Id is null);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name must not be empty")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters")
            .When(x => x.Name is not null);

        RuleFor(x => x.Quantity.Value)
            .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1")
            .When(x => x.Quantity is { IsSet: true, Value: not null })
            .OverridePropertyName("Quantity");

        RuleFor(x => x.Icon.Value)
            .MaximumLength(50).WithMessage("Icon must not exceed 50 characters")
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$").WithMessage("Icon must be a kebab-case key (e.g. \"air-conditioner\")")
            .When(x => x.Icon is { IsSet: true, Value: not null })
            .OverridePropertyName("Icon");
    }
}
