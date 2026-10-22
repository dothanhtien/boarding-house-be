using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.RoomAssets;

public record SplitRoomAssetRequest
{
    public required int Quantity { get; init; }
    public required AssetCondition NewCondition { get; init; }
    public string? Note { get; init; }
}
