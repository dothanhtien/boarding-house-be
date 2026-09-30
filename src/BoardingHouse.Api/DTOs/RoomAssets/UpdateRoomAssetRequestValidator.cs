using FluentValidation;

namespace BoardingHouse.Api.DTOs.RoomAssets;

public class UpdateRoomAssetRequestValidator : AbstractValidator<UpdateRoomAssetRequest>
{
    public UpdateRoomAssetRequestValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters")
            .When(x => x.Name.IsSet)
            .OverridePropertyName("Name");

        RuleFor(x => x.Quantity.Value)
            .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1")
            .When(x => x.Quantity.IsSet)
            .OverridePropertyName("Quantity");

        RuleFor(x => x.PurchaseUnitPrice.Value)
            .GreaterThan(0).WithMessage("PurchaseUnitPrice must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("PurchaseUnitPrice must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.PurchaseUnitPrice is { IsSet: true, Value: not null })
            .OverridePropertyName("PurchaseUnitPrice");
    }
}
