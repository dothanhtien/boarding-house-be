using FluentValidation;

namespace BoardingHouse.Api.DTOs.RoomAssets;

public class CreateRoomAssetRequestValidator : AbstractValidator<CreateRoomAssetRequest>
{
    public CreateRoomAssetRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1");

        RuleFor(x => x.PurchaseUnitPrice)
            .GreaterThan(0).WithMessage("PurchaseUnitPrice must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("PurchaseUnitPrice must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.PurchaseUnitPrice is not null);
    }
}
