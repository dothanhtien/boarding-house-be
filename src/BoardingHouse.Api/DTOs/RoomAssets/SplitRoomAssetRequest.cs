using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.RoomAssets;

public record SplitRoomAssetRequest
{
    public required int Quantity { get; init; }
    public required AssetCondition NewCondition { get; init; }
    public string? Note { get; init; }
}
