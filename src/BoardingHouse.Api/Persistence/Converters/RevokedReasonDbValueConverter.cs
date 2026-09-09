using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class RevokedReasonDbValueConverter
{
    public static readonly ValueConverter<RevokedReason, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<RevokedReason, string>
        {
            [RevokedReason.Rotation] = "ROTATION",
            [RevokedReason.Logout] = "LOGOUT",
            [RevokedReason.Suspicious] = "SUSPICIOUS"
        });
}
