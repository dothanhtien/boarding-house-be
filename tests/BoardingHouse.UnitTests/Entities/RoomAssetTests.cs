using BoardingHouse.UnitTests.Common;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class RoomAssetTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    internal static MediaAsset NewPhoto() => TestEntities.New<MediaAsset>(new
    {
        FileName = "photo.png",
        StorageKey = $"test/{Guid.NewGuid():N}",
        FileUrl = "https://storage.test/photo.png",
        MimeType = "image/png",
        CreatedBy = SentinelActors.System
    });

    private static RoomAsset NewAsset(int quantity = 4) =>
        RoomAsset.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Chair", quantity, new DateOnly(2026, 1, 1), 500_000, "Wooden",
            photos: [], SentinelActors.System, Now).Asset;

    [Fact]
    public void Create_StartsGoodWithAFirstHistoryEntryHoldingThePhotos()
    {
        var id = Guid.NewGuid();
        var photos = new[] { NewPhoto(), NewPhoto() };

        var (asset, history) = RoomAsset.Create(
            id, Guid.NewGuid(), "Bed", 1, purchaseDate: null, purchaseUnitPrice: null, note: "Double",
            photos, ActorId, Now);

        Assert.Equal((id, AssetCondition.Good, "Double", ActorId), (asset.Id, asset.Condition, asset.Note, asset.CreatedBy));
        Assert.Equal((asset.Id, (AssetCondition?)null, AssetCondition.Good, (string?)null, (Guid?)ActorId, Now),
            (history.AssetId, history.OldCondition, history.NewCondition, history.Note, history.ChangedBy, history.ChangedAt));
        Assert.Equal([0, 1], history.Media.Select(m => m.SortOrder));
        Assert.Equal(photos.Select(p => p.Id), history.Media.Select(m => m.MediaAssetId));
    }

    [Fact]
    public void Create_QuantityBelowOne_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => NewAsset(quantity: 0));

        Assert.Equal(("Quantity must be at least 1", DomainErrorKind.Validation), (ex.Message, ex.Kind));
    }

    [Fact]
    public void ChangeCondition_RecordsOldAndNewConditionThenUpdatesTheAsset()
    {
        var asset = NewAsset();

        var history = asset.ChangeCondition(AssetCondition.Damaged, "Leg cracked", [NewPhoto()], ActorId, Now);

        Assert.Equal(AssetCondition.Damaged, asset.Condition);
        Assert.Equal((asset.Id, (AssetCondition?)AssetCondition.Good, AssetCondition.Damaged, "Leg cracked"),
            (history.AssetId, history.OldCondition, history.NewCondition, history.Note));
        Assert.Single(history.Media);
    }

    [Fact]
    public void ChangeCondition_TooManyPhotos_ThrowsAndKeepsCondition()
    {
        var asset = NewAsset();
        var photos = Enumerable.Range(0, RoomAssetConditionHistory.MaxMedia + 1).Select(_ => NewPhoto()).ToList();

        var ex = Assert.Throws<DomainException>(() =>
            asset.ChangeCondition(AssetCondition.Damaged, null, photos, ActorId, Now));

        Assert.Equal($"A condition entry must not have more than {RoomAssetConditionHistory.MaxMedia} photos", ex.Message);
        Assert.Equal(AssetCondition.Good, asset.Condition);
    }

    [Fact]
    public void Split_MovesUnitsToANewRowWithItsOwnCondition()
    {
        var asset = NewAsset(quantity: 4);

        var (split, history) = asset.Split(1, AssetCondition.Broken, "Broken seat", ActorId, Now);

        Assert.Equal(3, asset.Quantity);
        Assert.Equal(AssetCondition.Good, asset.Condition);
        Assert.Equal(
            (asset.RoomId, "Chair", 1, asset.PurchaseDate, asset.PurchaseUnitPrice, AssetCondition.Broken, "Wooden", (Guid?)asset.Id, ActorId),
            (split.RoomId, split.Name, split.Quantity, split.PurchaseDate, split.PurchaseUnitPrice, split.Condition, split.Note, split.SplitFromAssetId, split.CreatedBy));
        Assert.Equal((split.Id, (AssetCondition?)AssetCondition.Good, AssetCondition.Broken, "Broken seat"),
            (history.AssetId, history.OldCondition, history.NewCondition, history.Note));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Split_WholeRowOrMore_Throws(int quantity)
    {
        var asset = NewAsset(quantity: 4);

        var ex = Assert.Throws<DomainException>(() => asset.Split(quantity, AssetCondition.Broken, null, ActorId, Now));

        Assert.StartsWith("Quantity must be less than the asset's quantity (4)", ex.Message);
        Assert.Equal(4, asset.Quantity);
    }

    [Fact]
    public void Update_ReplacesTheEditableFields()
    {
        var asset = NewAsset();

        asset.Update("Stool", 2, purchaseDate: null, purchaseUnitPrice: null, note: null);

        Assert.Equal(("Stool", 2, (DateOnly?)null, (decimal?)null, (string?)null),
            (asset.Name, asset.Quantity, asset.PurchaseDate, asset.PurchaseUnitPrice, asset.Note));
    }

    [Fact]
    public void Update_QuantityBelowOne_Throws()
    {
        Assert.Throws<DomainException>(() => NewAsset().Update("Chair", 0, null, null, null));
    }

    [Fact]
    public void Update_BlankName_Throws()
    {
        Assert.Throws<DomainException>(() => NewAsset().Update(" ", 1, null, null, null));
    }

    [Fact]
    public void Update_PurchaseUnitPriceNotPositive_Throws()
    {
        Assert.Throws<DomainException>(() => NewAsset().Update("Chair", 1, null, 0, null));
    }
}
