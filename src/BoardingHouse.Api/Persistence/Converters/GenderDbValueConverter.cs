using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class GenderDbValueConverter
{
    public static readonly ValueConverter<Gender, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<Gender, string>
        {
            [Gender.Male] = "MALE",
            [Gender.Female] = "FEMALE",
            [Gender.Other] = "OTHER"
        });
}
