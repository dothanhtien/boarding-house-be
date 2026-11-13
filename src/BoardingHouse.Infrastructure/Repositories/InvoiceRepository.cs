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

    public Task<Invoice?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithLeaseDetails(Context.Invoices.IgnoreQueryFilters().Where(i => i.DeletedAt == null))
            .AsNoTracking()
            .Include(i => i.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<int> NextNumberAsync(
        Guid organizationId, BillingMonth billingMonth, CancellationToken cancellationToken = default)
    {
        var firstDay = billingMonth.FirstDay;
        var numbers = await Context.Database
            .SqlQuery<int>($"""
                INSERT INTO invoice_number_sequences (organization_id, billing_month, last_number)
                VALUES ({organizationId}, {firstDay}, 1)
                ON CONFLICT (organization_id, billing_month)
                DO UPDATE SET last_number = invoice_number_sequences.last_number + 1
                RETURNING last_number AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return numbers.Single();
    }

    private static IQueryable<Invoice> WithLeaseDetails(IQueryable<Invoice> invoices) =>
        invoices.Include(i => i.Lease!).ThenInclude(l => l.Room!).ThenInclude(r => r.Property);
}
