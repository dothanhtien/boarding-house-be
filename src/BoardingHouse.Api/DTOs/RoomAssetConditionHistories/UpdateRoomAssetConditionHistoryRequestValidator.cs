using FluentValidation;
using BoardingHouse.Api.DTOs.Media;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public class UpdateRoomAssetConditionHistoryRequestValidator
    : AbstractValidator<UpdateRoomAssetConditionHistoryRequest>
{
    public UpdateRoomAssetConditionHistoryRequestValidator()
    {
        RuleFor(x => x.RemoveMediaIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("RemoveMediaIds must not contain duplicates");

        RuleFor(x => x.Files).ValidImageFiles();
    }
}
