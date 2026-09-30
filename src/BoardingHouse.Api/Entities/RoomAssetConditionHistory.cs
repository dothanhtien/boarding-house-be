using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class RoomAssetConditionHistory : Entity
{
    public Guid AssetId { get; set; }
    public RoomAsset? Asset { get; set; }

    public AssetCondition? OldCondition { get; set; }
    public required AssetCondition NewCondition { get; set; }
    public string? Note { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
