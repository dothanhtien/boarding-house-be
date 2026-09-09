using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class EnumDbValueConverter
{
    public static ValueConverter<TEnum, string> Create<TEnum>(IReadOnlyDictionary<TEnum, string> map)
        where TEnum : struct, Enum
    {
        var reverseMap = map.ToDictionary(kv => kv.Value, kv => kv.Key);
        return new ValueConverter<TEnum, string>(v => map[v], v => reverseMap[v]);
    }
}
