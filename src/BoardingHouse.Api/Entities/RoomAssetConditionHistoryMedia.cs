using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities;

public class RoomAssetConditionHistoryMedia : BaseEntity
{
    public Guid ConditionHistoryId { get; set; }
    public RoomAssetConditionHistory? ConditionHistory { get; set; }

    public Guid MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }

    public int SortOrder { get; set; }
}
