using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class PaymentTransactionStatusDbValueConverter
{
    public static readonly ValueConverter<PaymentTransactionStatus, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<PaymentTransactionStatus, string>
        {
            [PaymentTransactionStatus.Pending] = "PENDING",
            [PaymentTransactionStatus.Success] = "SUCCESS",
            [PaymentTransactionStatus.Failed] = "FAILED"
        });
}
