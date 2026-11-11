using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class RoomAssetConditionHistoryMedia : BaseEntity
{
    public Guid ConditionHistoryId { get; private set; }
    public RoomAssetConditionHistory? ConditionHistory { get; private set; }

    public Guid MediaAssetId { get; private set; }
    public MediaAsset? MediaAsset { get; private set; }

    public int SortOrder { get; private set; }

    private RoomAssetConditionHistoryMedia() { }

    public RoomAssetConditionHistoryMedia(Guid conditionHistoryId, MediaAsset mediaAsset, int sortOrder, Guid createdBy)
    {
        ConditionHistoryId = conditionHistoryId;
        MediaAssetId = mediaAsset.Id;
        MediaAsset = mediaAsset;
        SortOrder = sortOrder;
        CreatedBy = createdBy;
    }
}
