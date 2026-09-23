using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class RoomCategoryDbValueConverter
{
    public static readonly ValueConverter<RoomCategory, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<RoomCategory, string>
        {
            [RoomCategory.Standard] = "STANDARD",
            [RoomCategory.Studio] = "STUDIO",
            [RoomCategory.Duplex] = "DUPLEX",
            [RoomCategory.Apartment] = "APARTMENT"
        });
}
