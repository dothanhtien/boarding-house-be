using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class RoomAssetConditionHistory : Entity, IUpdateAuditable
{
    public const int MaxMedia = 10;

    public Guid AssetId { get; set; }
    public RoomAsset? Asset { get; set; }

    public AssetCondition? OldCondition { get; set; }
    public required AssetCondition NewCondition { get; set; }
    public string? Note { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<RoomAssetConditionHistoryMedia> Media { get; set; } = [];
}
