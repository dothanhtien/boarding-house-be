using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class InvoiceRepository(AppDbContext context) : Repository<Invoice>(context), IInvoiceRepository
{
    public Task<bool> ExistsLiveForLeaseMonthAsync(
        Guid leaseId, BillingMonth billingMonth, CancellationToken cancellationToken = default) =>
        Context.Invoices.AnyAsync(
            i => i.LeaseId == leaseId && i.BillingMonth == billingMonth && i.Status != InvoiceStatus.Cancelled,
            cancellationToken);

    public Task<bool> ExistsLiveDepositItemAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        InvoiceItemQueries.Live(Context).AnyAsync(
            i => i.SourceType == InvoiceItemSourceType.Deposit && i.SourceId == leaseId,
            cancellationToken);
}
