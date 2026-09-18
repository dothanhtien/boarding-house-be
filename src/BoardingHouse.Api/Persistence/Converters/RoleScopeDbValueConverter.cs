using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class RoleScopeDbValueConverter
{
    public static readonly ValueConverter<RoleScope, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<RoleScope, string>
        {
            [RoleScope.Platform] = "PLATFORM",
            [RoleScope.Organization] = "ORGANIZATION"
        }
    );
}
