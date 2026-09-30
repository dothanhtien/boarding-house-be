using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public record RoomAssetConditionHistoryResponse
{
    public required Guid Id { get; init; }
    public required Guid AssetId { get; init; }
    public AssetCondition? OldCondition { get; init; }
    public required AssetCondition NewCondition { get; init; }
    public string? Note { get; init; }
    public Guid? ChangedBy { get; init; }
    public required DateTimeOffset ChangedAt { get; init; }
}
