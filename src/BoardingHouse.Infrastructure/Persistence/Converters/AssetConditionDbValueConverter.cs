using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

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
