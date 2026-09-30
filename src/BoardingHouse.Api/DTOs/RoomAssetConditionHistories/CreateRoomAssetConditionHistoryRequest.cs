using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public record CreateRoomAssetConditionHistoryRequest
{
    public required AssetCondition NewCondition { get; init; }
    public string? Note { get; init; }
}
