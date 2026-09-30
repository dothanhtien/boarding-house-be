using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class AssetConditionDbValueConverter
{
    public static readonly ValueConverter<AssetCondition, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<AssetCondition, string>
        {
            [AssetCondition.Good] = "GOOD",
            [AssetCondition.Damaged] = "DAMAGED",
            [AssetCondition.Broken] = "BROKEN"
        });
}
