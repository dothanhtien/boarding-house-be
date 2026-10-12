using BoardingHouse.Api.DTOs.Media;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public class CreateRoomAssetConditionHistoryRequestValidator
    : AbstractValidator<CreateRoomAssetConditionHistoryRequest>
{
    public CreateRoomAssetConditionHistoryRequestValidator()
    {
        RuleFor(x => x.NewCondition)
            .NotNull().WithMessage("NewCondition is required")
            .IsInEnum().WithMessage("NewCondition is invalid");

        RuleFor(x => x.Files).ValidImageFiles();
    }
}
