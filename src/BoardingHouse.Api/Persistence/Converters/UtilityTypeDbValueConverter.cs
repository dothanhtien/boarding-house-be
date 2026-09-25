using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class UtilityTypeDbValueConverter
{
    public static readonly ValueConverter<UtilityType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<UtilityType, string>
        {
            [UtilityType.Electricity] = "ELECTRICITY",
            [UtilityType.Water] = "WATER",
            [UtilityType.Internet] = "INTERNET",
            [UtilityType.Parking] = "PARKING",
            [UtilityType.Garbage] = "GARBAGE",
            [UtilityType.Other] = "OTHER"
        }
    );
}
