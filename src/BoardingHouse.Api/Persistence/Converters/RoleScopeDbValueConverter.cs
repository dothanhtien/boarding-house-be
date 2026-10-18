using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

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
