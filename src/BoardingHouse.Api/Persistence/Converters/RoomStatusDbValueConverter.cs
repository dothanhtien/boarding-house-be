using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class RoomStatusDbValueConverter
{
    public static readonly ValueConverter<RoomStatus, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<RoomStatus, string>
        {
            [RoomStatus.Available] = "AVAILABLE",
            [RoomStatus.Reserved] = "RESERVED",
            [RoomStatus.Occupied] = "OCCUPIED",
            [RoomStatus.Maintenance] = "MAINTENANCE"
        });
}
