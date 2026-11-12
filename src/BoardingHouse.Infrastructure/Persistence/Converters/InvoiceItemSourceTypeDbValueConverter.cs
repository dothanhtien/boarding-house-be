using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class InvoiceItemSourceTypeDbValueConverter
{
    public static readonly ValueConverter<InvoiceItemSourceType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<InvoiceItemSourceType, string>
        {
            [InvoiceItemSourceType.Rent] = "RENT",
            [InvoiceItemSourceType.Deposit] = "DEPOSIT",
            [InvoiceItemSourceType.Utility] = "UTILITY",
            [InvoiceItemSourceType.Parking] = "PARKING",
            [InvoiceItemSourceType.Custom] = "CUSTOM",
        });
}
