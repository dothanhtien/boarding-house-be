using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class LeaseStatusDbValueConverter
{
    public static readonly ValueConverter<LeaseStatus, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<LeaseStatus, string>
        {
            [LeaseStatus.Draft] = "DRAFT",
            [LeaseStatus.Active] = "ACTIVE",
            [LeaseStatus.Expired] = "EXPIRED",
            [LeaseStatus.Terminated] = "TERMINATED"
        });
}
