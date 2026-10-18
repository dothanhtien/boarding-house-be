using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

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
