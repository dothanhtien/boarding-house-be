using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public record CreateRoomAssetConditionHistoryRequest
{
    public AssetCondition? NewCondition { get; init; }
    public string? Note { get; init; }
    public List<IFormFile>? Files { get; init; }
}
