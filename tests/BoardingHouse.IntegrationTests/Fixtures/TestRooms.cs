using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.IntegrationTests.Fixtures;

// Room's constructor is private: tests build rooms through Room.Create like the service does
internal static class TestRooms
{
    public static Room New(
        Guid? propertyId = null,
        string roomNumber = "101",
        int? capacity = null,
        decimal? monthlyRent = null,
        decimal? depositAmount = null,
        Guid? createdBy = null) =>
        Room.Create(
            propertyId ?? Guid.NewGuid(),
            new RoomDetails(roomNumber, RoomCategory.Standard, FloorNumber: null, Area: null, capacity, monthlyRent, depositAmount, Note: null),
            amenities: [],
            createdBy ?? SentinelActors.System);
}
