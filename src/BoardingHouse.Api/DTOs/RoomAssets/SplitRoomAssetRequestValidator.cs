using FluentValidation;

namespace BoardingHouse.Api.DTOs.RoomAssets;

public class SplitRoomAssetRequestValidator : AbstractValidator<SplitRoomAssetRequest>
{
    public SplitRoomAssetRequestValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1");

        RuleFor(x => x.NewCondition).IsInEnum().WithMessage("NewCondition is invalid");
    }
}
