using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class RoomMedia : BaseEntity
{
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }

    public Guid MediaAssetId { get; private set; }
    public MediaAsset? MediaAsset { get; private set; }

    public int SortOrder { get; private set; }
    public bool IsCover { get; private set; }

    private RoomMedia() { }

    public RoomMedia(Guid roomId, MediaAsset mediaAsset, int sortOrder, bool isCover, Guid createdBy)
    {
        RoomId = roomId;
        MediaAssetId = mediaAsset.Id;
        MediaAsset = mediaAsset;
        SortOrder = sortOrder;
        IsCover = isCover;
        CreatedBy = createdBy;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void SetCover(bool isCover) => IsCover = isCover;
}
