using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class RoomAsset : BaseEntity
{
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public required string Name { get; set; }
    public int Quantity { get; set; } = 1;
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseUnitPrice { get; set; }
    public AssetCondition Condition { get; set; } = AssetCondition.Good;
    public string? Note { get; set; }

    // Set when this row was split off another asset (e.g. 1 of 4 chairs damaged): the row it came from
    public Guid? SplitFromAssetId { get; set; }
}
