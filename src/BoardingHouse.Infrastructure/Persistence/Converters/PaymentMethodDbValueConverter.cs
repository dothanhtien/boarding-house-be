using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class PaymentMethodDbValueConverter
{
    public static readonly ValueConverter<PaymentMethod, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<PaymentMethod, string>
        {
            [PaymentMethod.Cash] = "CASH",
            [PaymentMethod.BankTransfer] = "BANK_TRANSFER",
            [PaymentMethod.Momo] = "MOMO",
            [PaymentMethod.ZaloPay] = "ZALOPAY",
            [PaymentMethod.VnPay] = "VNPAY"
        });
}
