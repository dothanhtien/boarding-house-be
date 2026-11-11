using BoardingHouse.UnitTests.Common;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class RoomTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    private static Room NewRoom(params string[] amenityNames)
    {
        var room = TestRooms.New();
        room.ApplyAmenityChanges(amenityNames.Select(name => new AmenityChange { Name = name }).ToList(), SentinelActors.System);
        return room;
    }

    private static RoomAmenity AmenityOf(Room room, string name) => room.Amenities.Single(a => a.Name == name);

    [Fact]
    public void ApplyAmenityChanges_AddsUpdatesAndRemoves()
    {
        var room = NewRoom("WiFi", "Fan");
        var wifi = AmenityOf(room, "WiFi");
        var fan = AmenityOf(room, "Fan");

        var added = room.ApplyAmenityChanges(
        [
            new AmenityChange { Id = wifi.Id, Icon = "wifi" },
            new AmenityChange { Id = fan.Id, IsDeleted = true },
            new AmenityChange { Name = "Air conditioner", Quantity = 2 }
        ], ActorId);

        var airConditioner = Assert.Single(added);
        Assert.Equal((room.Id, "Air conditioner", (int?)2, ActorId),
            (airConditioner.RoomId, airConditioner.Name, airConditioner.Quantity, airConditioner.CreatedBy));
        Assert.Equal(("WiFi", "wifi"), (wifi.Name, wifi.Icon));
        Assert.Equal([wifi, airConditioner], room.Amenities.OrderBy(a => a.Name == "WiFi" ? 0 : 1));
    }

    [Fact]
    public void ApplyAmenityChanges_NameNullOnExisting_KeepsName()
    {
        var room = NewRoom("WiFi");
        var wifi = AmenityOf(room, "WiFi");

        room.ApplyAmenityChanges([new AmenityChange { Id = wifi.Id, Quantity = null }], ActorId);

        Assert.Equal("WiFi", wifi.Name);
    }

    [Fact]
    public void ApplyAmenityChanges_NameOnExisting_RenamesIt()
    {
        var room = NewRoom("WiFi");
        var wifi = AmenityOf(room, "WiFi");

        room.ApplyAmenityChanges([new AmenityChange { Id = wifi.Id, Name = "WiFi 6" }], ActorId);

        Assert.Equal("WiFi 6", wifi.Name);
    }

    [Fact]
    public void ApplyAmenityChanges_UnknownId_ThrowsNotFound()
    {
        var ex = Assert.Throws<DomainException>(() =>
            NewRoom().ApplyAmenityChanges([new AmenityChange { Id = Guid.NewGuid(), Name = "x" }], ActorId));

        Assert.Equal(DomainErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void ApplyAmenityChanges_OverLimit_Throws()
    {
        var room = NewRoom(Enumerable.Range(0, Room.MaxAmenities).Select(i => $"A{i}").ToArray());

        Assert.Throws<DomainException>(() => room.ApplyAmenityChanges([new AmenityChange { Name = "One more" }], ActorId));
    }

    [Fact]
    public void ChangeStatus_ToMaintenance_ChangesStatus()
    {
        var room = NewRoom();

        room.ChangeStatus(RoomStatus.Maintenance, hasActiveLease: false);

        Assert.Equal(RoomStatus.Maintenance, room.RoomStatus);
    }

    [Fact]
    public void ChangeStatus_ToOccupied_ThrowsValidation()
    {
        var ex = Assert.Throws<DomainException>(() => NewRoom().ChangeStatus(RoomStatus.Occupied, hasActiveLease: false));

        Assert.Equal(("Room status OCCUPIED is managed by leases", DomainErrorKind.Validation), (ex.Message, ex.Kind));
    }

    [Fact]
    public void ChangeStatus_WithActiveLease_ThrowsConflict()
    {
        var room = NewRoom();
        room.MarkOccupied();

        var ex = Assert.Throws<DomainException>(() => room.ChangeStatus(RoomStatus.Available, hasActiveLease: true));

        Assert.Equal(("Room has an active lease", DomainErrorKind.Conflict), (ex.Message, ex.Kind));
        Assert.Equal(RoomStatus.Occupied, room.RoomStatus);
    }

    [Fact]
    public void ChangeStatus_SameStatus_IsANoOpEvenWithActiveLease()
    {
        var room = NewRoom();

        room.ChangeStatus(RoomStatus.Available, hasActiveLease: true);

        Assert.Equal(RoomStatus.Available, room.RoomStatus);
    }

    [Fact]
    public void AddMedia_AppendsAndMakesTheFirstOneCoverOnlyWhenNoneYet()
    {
        var room = NewRoom();
        var first = room.AddMedia([RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto()], ActorId);
        var second = room.AddMedia([RoomAssetTests.NewPhoto()], ActorId);

        Assert.Equal([0, 1, 2], first.Concat(second).Select(m => m.SortOrder));
        Assert.Equal([true, false, false], first.Concat(second).Select(m => m.IsCover));
        Assert.All(room.Media, m => Assert.Equal((room.Id, ActorId), (m.RoomId, m.CreatedBy)));
    }

    [Fact]
    public void AddMedia_OverTheLimit_Throws()
    {
        var room = NewRoom();
        room.AddMedia(Enumerable.Range(0, Room.MaxMedia).Select(_ => RoomAssetTests.NewPhoto()).ToList(), ActorId);

        var ex = Assert.Throws<DomainException>(() => room.AddMedia([RoomAssetTests.NewPhoto()], ActorId));

        Assert.Equal($"A room must not have more than {Room.MaxMedia} media", ex.Message);
        Assert.Equal(Room.MaxMedia, room.Media.Count);
    }

    [Fact]
    public void ReorderMedia_SetsSortOrderFromTheList()
    {
        var room = NewRoom();
        var media = room.AddMedia([RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto()], ActorId);

        room.ReorderMedia([media[1].MediaAssetId, media[0].MediaAssetId]);

        Assert.Equal((1, 0), (media[0].SortOrder, media[1].SortOrder));
    }

    [Fact]
    public void ReorderMedia_MissingOne_Throws()
    {
        var room = NewRoom();
        var media = room.AddMedia([RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto()], ActorId);

        var ex = Assert.Throws<DomainException>(() => room.ReorderMedia([media[0].MediaAssetId]));

        Assert.Equal("Order must list every media of the room exactly once", ex.Message);
    }

    [Fact]
    public void ReleaseCoverForThenSetCover_MovesTheCover()
    {
        var room = NewRoom();
        var media = room.AddMedia([RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto()], ActorId);

        room.ReleaseCoverFor(media[1].MediaAssetId);
        Assert.DoesNotContain(room.Media, m => m.IsCover);
        room.SetCover(media[1].MediaAssetId);

        Assert.Equal((false, true), (media[0].IsCover, media[1].IsCover));
    }

    [Fact]
    public void ReleaseCoverFor_UnknownMedia_ThrowsNotFoundAndKeepsCover()
    {
        var room = NewRoom();
        var cover = room.AddMedia([RoomAssetTests.NewPhoto()], ActorId)[0];
        var unknown = Guid.NewGuid();

        var ex = Assert.Throws<DomainException>(() => room.ReleaseCoverFor(unknown));

        Assert.Equal(($"Room media '{unknown}' not found", DomainErrorKind.NotFound), (ex.Message, ex.Kind));
        Assert.True(cover.IsCover);
    }

    [Fact]
    public void RemoveMediaThenApplyPendingCoverChange_PromotesTheFirstRemainingMedia()
    {
        var room = NewRoom();
        var media = room.AddMedia(
            [RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto()], ActorId);
        room.ReorderMedia([media[0].MediaAssetId, media[2].MediaAssetId, media[1].MediaAssetId]);

        var removed = room.RemoveMedia(media[0].MediaAssetId);
        Assert.True(room.HasPendingCoverChange);
        Assert.DoesNotContain(room.Media, m => m.IsCover);

        room.ApplyPendingCoverChange();

        Assert.True(removed.IsCover);
        Assert.Equal([media[2], media[1]], room.Media.OrderBy(m => m.SortOrder));
        Assert.True(media[2].IsCover);
    }

    [Fact]
    public void MarkOccupied_UnderMaintenance_ThrowsConflict()
    {
        var room = NewRoom();
        room.ChangeStatus(RoomStatus.Maintenance, hasActiveLease: false);

        var ex = Assert.Throws<DomainException>(room.MarkOccupied);

        Assert.Equal((DomainErrorKind.Conflict, RoomStatus.Maintenance), (ex.Kind, room.RoomStatus));
    }

    [Fact]
    public void MarkAvailable_OnlyReleasesAnOccupiedRoom()
    {
        var room = NewRoom();
        room.ChangeStatus(RoomStatus.Maintenance, hasActiveLease: false);

        room.MarkAvailable();

        Assert.Equal(RoomStatus.Maintenance, room.RoomStatus);
    }

    [Theory]
    [InlineData(" ", null, null, "RoomNumber is required")]
    [InlineData("101", 0, null, "Capacity must be greater than 0")]
    [InlineData("101", null, 0d, "MonthlyRent must be greater than 0")]
    public void Update_InvalidDetails_Throws(string roomNumber, int? capacity, double? monthlyRent, string message)
    {
        var room = NewRoom();

        var ex = Assert.Throws<DomainException>(() => room.Update(room.Details with
        {
            RoomNumber = roomNumber,
            Capacity = capacity,
            MonthlyRent = (decimal?)monthlyRent
        }));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void ApplyAmenityChanges_AddWithoutName_Throws()
    {
        Assert.Throws<DomainException>(() => NewRoom().ApplyAmenityChanges([new AmenityChange { Quantity = 2 }], ActorId));
    }

    [Fact]
    public void RemoveMedia_NotTheCover_HasNoPendingCoverChange()
    {
        var room = NewRoom();
        var media = room.AddMedia([RoomAssetTests.NewPhoto(), RoomAssetTests.NewPhoto()], ActorId);

        room.RemoveMedia(media[1].MediaAssetId);

        Assert.False(room.HasPendingCoverChange);
        Assert.Throws<InvalidOperationException>(room.ApplyPendingCoverChange);
    }
}
