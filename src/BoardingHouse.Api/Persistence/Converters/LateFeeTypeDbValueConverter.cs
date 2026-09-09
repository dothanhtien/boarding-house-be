using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class LateFeeTypeDbValueConverter
{
    public static readonly ValueConverter<LateFeeType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<LateFeeType, string>
        {
            [LateFeeType.Fixed] = "FIXED",
            [LateFeeType.Percent] = "PERCENT"
        });
}
