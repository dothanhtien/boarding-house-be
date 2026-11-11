using BoardingHouse.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class BillingMonthDbValueConverter
{
    public static readonly ValueConverter<BillingMonth, DateOnly> Instance = new(
        month => month.FirstDay,
        firstDay => BillingMonth.FromFirstDay(firstDay));
}
