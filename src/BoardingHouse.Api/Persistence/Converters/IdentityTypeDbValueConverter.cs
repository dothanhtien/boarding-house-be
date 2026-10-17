using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class IdentityTypeDbValueConverter
{
    public static readonly ValueConverter<IdentityType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<IdentityType, string>
        {
            [IdentityType.CitizenIdentityCard] = "CITIZEN_IDENTITY_CARD",
            [IdentityType.Passport] = "PASSPORT",
            [IdentityType.Other] = "OTHER"
        });
}
