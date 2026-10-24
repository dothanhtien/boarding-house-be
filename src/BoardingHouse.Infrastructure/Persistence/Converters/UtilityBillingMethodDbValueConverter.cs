using BoardingHouse.Domain.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class UtilityBillingMethodDbValueConverter
{
    public static readonly ValueConverter<UtilityBillingMethod, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<UtilityBillingMethod, string>
        {
            [UtilityBillingMethod.Metered] = "METERED",
            [UtilityBillingMethod.Fixed] = "FIXED",
            [UtilityBillingMethod.PerTenant] = "PER_TENANT"
        });
}
