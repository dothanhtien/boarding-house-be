using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

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

    public async Task<PagedResult<Invoice>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        Guid? roomId,
        Guid? leaseId,
        InvoiceStatus? status,
        BillingMonth? billingMonth,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Invoice, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var invoices = WithLeaseDetails(Context.Invoices.IgnoreQueryFilters().Where(i => i.DeletedAt == null))
            .AsNoTracking();

        if (organizationId is not null)
        {
            invoices = invoices.Where(i => i.OrganizationId == organizationId);
        }
        else if (allowedOrganizationIds is not null)
        {
            invoices = invoices.Where(i => allowedOrganizationIds.Contains(i.OrganizationId));
        }

        if (propertyId is not null)
        {
            invoices = invoices.Where(i => i.Lease!.Room!.PropertyId == propertyId);
        }

        if (roomId is not null)
        {
            invoices = invoices.Where(i => i.Lease!.RoomId == roomId);
        }

        if (leaseId is not null)
        {
            invoices = invoices.Where(i => i.LeaseId == leaseId);
        }

        if (status is not null)
        {
            invoices = invoices.Where(i => i.Status == status);
        }

        if (billingMonth is { } month)
        {
            invoices = invoices.Where(i => i.BillingMonth == month);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            invoices = invoices.Where(i =>
                EF.Functions.ILike(i.InvoiceNumber, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(i.Lease!.LeaseCode, pattern, LikePattern.EscapeCharacter));
        }

        return await invoices.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }

    private static IQueryable<Invoice> WithLeaseDetails(IQueryable<Invoice> invoices) =>
        invoices.Include(i => i.Lease!).ThenInclude(l => l.Room!).ThenInclude(r => r.Property);
}
