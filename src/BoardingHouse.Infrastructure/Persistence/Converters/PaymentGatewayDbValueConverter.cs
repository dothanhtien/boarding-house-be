using BoardingHouse.Domain.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class PaymentGatewayDbValueConverter
{
    public static readonly ValueConverter<PaymentGateway, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<PaymentGateway, string>
        {
            [PaymentGateway.VnPay] = "VNPAY"
        });
}
