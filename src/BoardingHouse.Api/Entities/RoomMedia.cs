using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities;

public class RoomMedia : BaseEntity
{
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }

    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}
