using FluentValidation;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public class CreateRoomAssetConditionHistoryRequestValidator : AbstractValidator<CreateRoomAssetConditionHistoryRequest>
{
    public CreateRoomAssetConditionHistoryRequestValidator()
    {
        RuleFor(x => x.NewCondition).IsInEnum().WithMessage("NewCondition is invalid");
    }
}
