using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

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
