using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class MediaAssetTests
{
    private static MediaAsset NewAsset() => MediaAsset.Uploaded(
        organizationId: null, "a.png", StorageProvider.Cloudinary, "key", "https://x/a.png", "image/png", 10, SentinelActors.System);

    [Fact]
    public void Uploaded_RecordsUploaderAsCreator()
    {
        var asset = NewAsset();

        Assert.Equal((SentinelActors.System, SentinelActors.System), (asset.UploadedByUserId!.Value, asset.CreatedBy));
        Assert.Null(asset.EntityType);
    }

    [Fact]
    public void AssignTo_SetsPurposeAndOwner()
    {
        var asset = NewAsset();
        var userId = Guid.NewGuid();

        asset.AssignTo(MediaAssetEntityType.UserAvatar, userId);

        Assert.Equal((MediaAssetEntityType.UserAvatar, userId), (asset.EntityType!.Value, asset.EntityId!.Value));
    }

    [Fact]
    public void AssignTo_Twice_Throws()
    {
        var asset = NewAsset();
        asset.AssignTo(MediaAssetEntityType.RoomMedia);

        Assert.Throws<InvalidOperationException>(() => asset.AssignTo(MediaAssetEntityType.RoomMedia));
    }
}
