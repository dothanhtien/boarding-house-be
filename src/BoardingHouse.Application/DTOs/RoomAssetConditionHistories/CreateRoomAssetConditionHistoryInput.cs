using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.RoomAssetConditionHistories;

public record CreateRoomAssetConditionHistoryInput
{
    public required AssetCondition NewCondition { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<IFileUpload> Files { get; init; } = [];
}
