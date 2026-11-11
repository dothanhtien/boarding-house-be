using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class RoomAssetConditionHistoryTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    // Entry built through the aggregate root, as the service does
    private static RoomAssetConditionHistory NewEntry(int photoCount)
    {
        var (asset, _) = RoomAsset.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Bed", 1, null, null, null, photos: [], SentinelActors.System, Now);
        var photos = Enumerable.Range(0, photoCount).Select(_ => RoomAssetTests.NewPhoto()).ToList();
        return asset.ChangeCondition(AssetCondition.Damaged, "Old note", photos, SentinelActors.System, Now);
    }

    [Fact]
    public void Update_RemovesAndAppendsPhotosAndStampsTheEntry()
    {
        var entry = NewEntry(photoCount: 2);
        var removedId = entry.Media.First().MediaAssetId;
        var added = RoomAssetTests.NewPhoto();
        var later = Now.AddHours(1);

        var removed = entry.Update(default, [removedId], [added], ActorId, later);

        Assert.Equal(removedId, Assert.Single(removed).Id);
        Assert.Equal([1, 2], entry.Media.Select(m => m.SortOrder));
        Assert.Equal(added.Id, entry.Media.Last().MediaAssetId);
        Assert.Equal("Old note", entry.Note);
        Assert.Equal(((DateTimeOffset?)later, (Guid?)ActorId), (entry.UpdatedAt, entry.UpdatedBy));
    }

    [Fact]
    public void Update_NoteOnly_ChangesNoteWithoutStamping()
    {
        var entry = NewEntry(photoCount: 1);

        var removed = entry.Update(note: null, [], [], ActorId, Now);

        Assert.Empty(removed);
        Assert.Null(entry.Note);
        Assert.Null(entry.UpdatedAt);
    }

    [Fact]
    public void Update_UnknownPhoto_ThrowsNotFound()
    {
        var entry = NewEntry(photoCount: 1);
        var unknown = Guid.NewGuid();

        var ex = Assert.Throws<DomainException>(() => entry.Update(default, [unknown], [], ActorId, Now));

        Assert.Equal(($"Room asset condition photo '{unknown}' not found", DomainErrorKind.NotFound), (ex.Message, ex.Kind));
    }

    [Fact]
    public void EnsureCanUpdatePhotos_FullEntry_RemovingOneMakesRoomForOne()
    {
        var entry = NewEntry(photoCount: RoomAssetConditionHistory.MaxMedia);

        entry.EnsureCanUpdatePhotos([entry.Media.First().MediaAssetId], addedCount: 1);
        var ex = Assert.Throws<DomainException>(() => entry.EnsureCanUpdatePhotos([], addedCount: 1));

        Assert.Equal($"A condition entry must not have more than {RoomAssetConditionHistory.MaxMedia} photos", ex.Message);
    }

    [Fact]
    public void RemoveAllPhotos_ReturnsEveryPhotoAndEmptiesTheEntry()
    {
        var entry = NewEntry(photoCount: 3);
        var expected = entry.Media.Select(m => m.MediaAsset!).ToList();

        var removed = entry.RemoveAllPhotos();

        Assert.Equal(expected, removed);
        Assert.Empty(entry.Media);
    }
}
