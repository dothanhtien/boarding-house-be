using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

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
