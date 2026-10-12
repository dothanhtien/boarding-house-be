using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomAssetCurrentPhotosResponse
{
    public required Guid ConditionHistoryId { get; init; }
    public required AssetCondition Condition { get; init; }
    public required DateTimeOffset ChangedAt { get; init; }
    public required bool IsLatest { get; init; }
    public required List<RoomAssetConditionHistoryMediaResponse> Media { get; init; }
}
