using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

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
