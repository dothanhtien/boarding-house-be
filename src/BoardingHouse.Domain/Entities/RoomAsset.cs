using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class RoomAsset : BaseEntity
{
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }

    public string Name { get; private set; } = null!;
    public int Quantity { get; private set; } = 1;
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchaseUnitPrice { get; private set; }
    public AssetCondition Condition { get; private set; } = AssetCondition.Good;
    public string? Note { get; private set; }

    // Set when this row was split off another asset (e.g. 1 of 4 chairs damaged): the row it came from
    public Guid? SplitFromAssetId { get; private set; }

    private RoomAsset() { }

    public static (RoomAsset Asset, RoomAssetConditionHistory History) Create(
        Guid id,
        Guid roomId,
        string name,
        int quantity,
        DateOnly? purchaseDate,
        decimal? purchaseUnitPrice,
        string? note,
        IReadOnlyList<MediaAsset> photos,
        Guid createdBy,
        DateTimeOffset now)
    {
        EnsureValid(name, quantity, purchaseUnitPrice);

        var asset = new RoomAsset
        {
            Id = id,
            RoomId = roomId,
            Name = name,
            Quantity = quantity,
            PurchaseDate = purchaseDate,
            PurchaseUnitPrice = purchaseUnitPrice,
            Condition = AssetCondition.Good,
            Note = note,
            CreatedBy = createdBy
        };

        var history = new RoomAssetConditionHistory(
            asset.Id, oldCondition: null, asset.Condition, note: null, photos, createdBy, now);

        return (asset, history);
    }

    public void Update(string name, int quantity, DateOnly? purchaseDate, decimal? purchaseUnitPrice, string? note)
    {
        EnsureValid(name, quantity, purchaseUnitPrice);

        Name = name;
        Quantity = quantity;
        PurchaseDate = purchaseDate;
        PurchaseUnitPrice = purchaseUnitPrice;
        Note = note;
    }

    public RoomAssetConditionHistory ChangeCondition(
        AssetCondition newCondition,
        string? note,
        IReadOnlyList<MediaAsset> photos,
        Guid changedBy,
        DateTimeOffset now)
    {
        var history = new RoomAssetConditionHistory(Id, Condition, newCondition, note, photos, changedBy, now);
        Condition = newCondition;

        return history;
    }

    public (RoomAsset Split, RoomAssetConditionHistory History) Split(
        int quantity,
        AssetCondition newCondition,
        string? note,
        Guid changedBy,
        DateTimeOffset now)
    {
        EnsureQuantityValid(quantity);

        if (quantity >= Quantity)
        {
            throw new DomainException(
                $"Quantity must be less than the asset's quantity ({Quantity}); to change the condition of the whole row, record a condition history entry instead");
        }

        var split = new RoomAsset
        {
            RoomId = RoomId,
            Name = Name,
            Quantity = quantity,
            PurchaseDate = PurchaseDate,
            PurchaseUnitPrice = PurchaseUnitPrice,
            Condition = newCondition,
            Note = Note,
            SplitFromAssetId = Id,
            CreatedBy = changedBy
        };

        Quantity -= quantity;

        var history = new RoomAssetConditionHistory(split.Id, Condition, newCondition, note, [], changedBy, now);

        return (split, history);
    }

    private static void EnsureValid(string name, int quantity, decimal? purchaseUnitPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required");
        }

        EnsureQuantityValid(quantity);

        if (purchaseUnitPrice <= 0)
        {
            throw new DomainException("PurchaseUnitPrice must be greater than 0");
        }
    }

    private static void EnsureQuantityValid(int quantity)
    {
        if (quantity < 1)
        {
            throw new DomainException("Quantity must be at least 1");
        }
    }
}
