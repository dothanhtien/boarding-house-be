using BoardingHouse.Domain.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class PaymentStatusDbValueConverter
{
    public static readonly ValueConverter<PaymentStatus, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<PaymentStatus, string>
        {
            [PaymentStatus.Pending] = "PENDING",
            [PaymentStatus.Confirmed] = "CONFIRMED",
            [PaymentStatus.Voided] = "VOIDED"
        });
}
