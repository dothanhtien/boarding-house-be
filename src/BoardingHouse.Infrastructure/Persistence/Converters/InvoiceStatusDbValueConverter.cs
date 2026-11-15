using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class InvoiceStatusDbValueConverter
{
    public static readonly ValueConverter<InvoiceStatus, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<InvoiceStatus, string>
        {
            [InvoiceStatus.Pending] = "PENDING",
            [InvoiceStatus.Partial] = "PARTIAL",
            [InvoiceStatus.Paid] = "PAID",
            [InvoiceStatus.Overdue] = "OVERDUE",
            [InvoiceStatus.Cancelled] = "CANCELLED"
        });
}
