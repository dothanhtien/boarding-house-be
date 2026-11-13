using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Infrastructure.Persistence;

internal static class InvoiceItemQueries
{
    public static IQueryable<InvoiceItem> Live(AppDbContext context) =>
        context.InvoiceItems
            .IgnoreQueryFilters()
            .Where(i => i.Invoice!.DeletedAt == null && i.Invoice.Status != InvoiceStatus.Cancelled);
}
