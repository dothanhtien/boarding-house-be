using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class PaymentTransactionTypeDbValueConverter
{
    public static readonly ValueConverter<PaymentTransactionType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<PaymentTransactionType, string>
        {
            [PaymentTransactionType.Checkout] = "CHECKOUT",
            [PaymentTransactionType.Callback] = "CALLBACK"
        });
}
