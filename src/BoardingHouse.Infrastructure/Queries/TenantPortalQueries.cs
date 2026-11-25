using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Queries;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Queries;

public class TenantPortalQueries(AppDbContext context) : ITenantPortalQueries
{

    private sealed class Stay
    {
        public Guid LeaseId { get; init; }
        public LeaseStatus LeaseStatus { get; init; }
        public DateOnly From { get; init; }
        public DateOnly? To { get; init; }
    }

    public async Task<TenancyResponse> GetTenancyAsync(Guid userId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var stays = StaysOf(userId);

        var total = await stays.Select(s => s.LeaseId).Distinct().CountAsync(cancellationToken);
        var active = total == 0
            ? 0
            : await stays
                .Where(s => s.LeaseStatus == LeaseStatus.Active && (s.To == null || s.To >= today))
                .Select(s => s.LeaseId)
                .Distinct()
                .CountAsync(cancellationToken);

        return new TenancyResponse { ActiveLeaseCount = active, TotalLeaseCount = total };
    }

    public Task<PagedResult<MyLeaseResponse>> SearchLeasesAsync(
        Guid userId, MyLeaseListQuery query, CancellationToken cancellationToken = default)
    {
        var leases = VisibleLeases(userId);

        if (query.Status is not null)
        {
            leases = leases.Where(l => l.Status == query.Status);
        }

        return leases.ToPagedResultAsync(
            query, l => l.StartDate, SortOrder.Desc, page => page.Select(ToMyLease(userId)), cancellationToken);
    }

    public async Task<MyLeaseDetailResponse?> GetLeaseAsync(Guid userId, Guid leaseId, CancellationToken cancellationToken = default)
    {
        var lease = await VisibleLeases(userId)
            .Where(l => l.Id == leaseId)
            .Select(ToMyLease(userId))
            .FirstOrDefaultAsync(cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var tenants = await context.LeaseTenants
            .IgnoreQueryFilters()
            .Where(t => t.LeaseId == leaseId && t.DeletedAt == null)
            .OrderByDescending(t => t.IsRepresentative)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => new MyLeaseRoommateResponse
            {
                FullName = t.User!.FullName,
                IsRepresentative = t.IsRepresentative,
                JoinedDate = t.JoinedDate ?? lease.StartDate,
                LeftDate = t.LeftDate
            })
            .ToListAsync(cancellationToken);

        var services = await context.LeaseUtilityServices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Service)
            .Where(s => s.LeaseId == leaseId)
            .OrderBy(s => s.Service!.Name)
            .ThenBy(s => s.ServiceId)
            .ToListAsync(cancellationToken);

        return new MyLeaseDetailResponse(lease)
        {
            Tenants = tenants,
            UtilityServices = services
                .Where(s => s.IsBillable)
                .Select(s => new MyLeaseUtilityServiceResponse
                {
                    ServiceId = s.ServiceId,
                    Name = s.Service!.Name,
                    Type = s.Service.Type,
                    BillingMethod = s.Service.BillingMethod,
                    Unit = s.Service.Unit,
                    UnitPrice = s.EffectiveUnitPrice,
                    Quantity = s.Quantity
                })
                .ToList()
        };
    }

    public Task<PagedResult<MyInvoiceResponse>> SearchInvoicesAsync(
        Guid userId, MyInvoiceListQuery query, CancellationToken cancellationToken = default)
    {
        var invoices = VisibleInvoices(userId);

        if (query.LeaseId is not null)
        {
            invoices = invoices.Where(i => i.LeaseId == query.LeaseId);
        }

        invoices = query.Status is not null
            ? invoices.Where(i => i.Status == query.Status)
            : invoices.Where(i => i.Status != InvoiceStatus.Cancelled);

        if (query.BillingMonth is { } day)
        {
            var month = BillingMonth.FromFirstDay(day);
            invoices = invoices.Where(i => i.BillingMonth == month);
        }

        return invoices.ToPagedResultAsync(
            query, i => i.BillingMonth, SortOrder.Desc, page => page.Select(ToMyInvoice), cancellationToken);
    }

    public async Task<MyInvoiceDetailResponse?> GetInvoiceAsync(Guid userId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await VisibleInvoices(userId)
            .Where(i => i.Id == invoiceId)
            .Select(ToMyInvoice)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var items = await context.InvoiceItems
            .Where(item => item.InvoiceId == invoiceId)
            .OrderBy(item => item.SortOrder)
            .Select(item => new MyInvoiceItemResponse
            {
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Amount,
                SourceType = item.SourceType
            })
            .ToListAsync(cancellationToken);

        return new MyInvoiceDetailResponse(invoice) { Items = items };
    }

    private IQueryable<Stay> StaysOf(Guid userId) =>
        context.LeaseTenants
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId
                && t.DeletedAt == null
                && t.Lease!.DeletedAt == null
                && t.Lease.Status != LeaseStatus.Draft)
            .Select(t => new Stay
            {
                LeaseId = t.LeaseId,
                LeaseStatus = t.Lease!.Status,
                From = t.JoinedDate ?? t.Lease.StartDate,
                To = t.LeftDate
            });

    private IQueryable<Lease> VisibleLeases(Guid userId)
    {
        var stays = StaysOf(userId);
        return context.Leases.IgnoreQueryFilters().Where(l => stays.Any(s => s.LeaseId == l.Id));
    }

    private static Expression<Func<Lease, MyLeaseResponse>> ToMyLease(Guid userId) => l => new MyLeaseResponse
    {
        Id = l.Id,
        OrganizationId = l.OrganizationId,
        OrganizationName = l.Organization!.Name,
        PropertyId = l.Room!.PropertyId,
        PropertyName = l.Room.Property!.Name,
        PropertyAddress = l.Room.Property.Address,
        RoomId = l.RoomId,
        RoomNumber = l.Room.RoomNumber,
        LeaseCode = l.LeaseCode,
        Status = l.Status,
        StartDate = l.StartDate,
        EndDate = l.EndDate,
        BillingDay = l.BillingDay,
        MonthlyRent = l.MonthlyRent,
        DepositAmount = l.DepositAmount,
        SignedAt = l.SignedAt,
        TerminatedAt = l.TerminatedAt,
        IsRepresentative = l.Tenants.Any(t => t.UserId == userId && t.DeletedAt == null && t.IsRepresentative),
        MyStays = l.Tenants
            .Where(t => t.UserId == userId && t.DeletedAt == null)
            .OrderBy(t => t.JoinedDate ?? l.StartDate)
            .Select(t => new MyStayResponse { JoinedDate = t.JoinedDate ?? l.StartDate, LeftDate = t.LeftDate })
            .ToList()
    };

    private IQueryable<Invoice> VisibleInvoices(Guid userId)
    {
        var stays = StaysOf(userId);
        return context.Invoices
            .IgnoreQueryFilters()
            .Where(i => i.DeletedAt == null
                && stays.Any(s => s.LeaseId == i.LeaseId
                    && s.From < EF.Property<DateOnly>(i, nameof(Invoice.BillingMonth)).AddMonths(1)
                    && (s.To == null || s.To >= EF.Property<DateOnly>(i, nameof(Invoice.BillingMonth)))));
    }

    private static readonly Expression<Func<Invoice, MyInvoiceResponse>> ToMyInvoice = i => new MyInvoiceResponse
    {
        Id = i.Id,
        OrganizationId = i.OrganizationId,
        OrganizationName = i.Organization!.Name,
        PropertyName = i.Lease!.Room!.Property!.Name,
        RoomNumber = i.Lease.Room.RoomNumber,
        LeaseId = i.LeaseId,
        LeaseCode = i.Lease.LeaseCode,
        InvoiceNumber = i.InvoiceNumber,
        BillingMonth = i.BillingMonth.FirstDay,
        DueDate = i.DueDate,
        Subtotal = i.Subtotal,
        VatRate = i.VatRate,
        VatAmount = i.VatAmount,
        LateFeeAmount = i.LateFeeAmount,
        DiscountAmount = i.DiscountAmount,
        DiscountReason = i.DiscountReason,
        TotalAmount = i.TotalAmount,
        PaidAmount = i.PaidAmount,
        RemainingAmount = i.TotalAmount - i.PaidAmount,
        Status = i.Status,
        SentAt = i.SentAt
    };
}
