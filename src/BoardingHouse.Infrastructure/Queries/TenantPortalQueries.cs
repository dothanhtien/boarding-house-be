using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Queries;

public class TenantPortalQueries(AppDbContext context, IStorageProvider storageProvider) : ITenantPortalQueries
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

    public Task<bool> IsLeaseVisibleAsync(Guid userId, Guid leaseId, CancellationToken cancellationToken = default) =>
        StaysOf(userId).AnyAsync(s => s.LeaseId == leaseId, cancellationToken);

    public async Task<PagedResult<MyUtilityReadingResponse>> SearchUtilityReadingsAsync(
        Guid userId, Guid leaseId, MyUtilityReadingListQuery query, CancellationToken cancellationToken = default)
    {
        var stays = StaysOf(userId).Where(s => s.LeaseId == leaseId);
        var liveUtilityItems = InvoiceItemQueries.Live(context).Where(i => i.SourceType == InvoiceItemSourceType.Utility);
        var images = context.MediaAssets
            .Where(m => m.EntityType == MediaAssetEntityType.UtilityReadingImage && m.DeletedAt == null);

        var readings = context.UtilityReadings
            .IgnoreQueryFilters()
            .Where(r => r.BilledLeaseId == leaseId
                && r.DeletedAt == null
                && stays.Any(s => s.From < EF.Property<DateOnly>(r, nameof(UtilityReading.ReadingMonth)).AddMonths(1)
                    && (s.To == null || s.To >= EF.Property<DateOnly>(r, nameof(UtilityReading.ReadingMonth)))));

        if (query.ServiceId is not null)
        {
            readings = readings.Where(r => r.ServiceId == query.ServiceId);
        }

        if (query.From is { } from)
        {
            var fromMonth = BillingMonth.FromFirstDay(from);
            readings = readings.Where(r => r.ReadingMonth >= fromMonth);
        }

        if (query.To is { } to)
        {
            var toMonth = BillingMonth.FromFirstDay(to);
            readings = readings.Where(r => r.ReadingMonth <= toMonth);
        }

        var page = await readings.ToPagedResultAsync(
            query,
            r => r.ReadingMonth,
            SortOrder.Desc,
            q => q
                .LeftJoin(images, r => r.Id, m => m.EntityId, (r, m) => new { Reading = r, Media = m })
                .Select(x => new
                {
                    Response = new MyUtilityReadingResponse
                    {
                        Id = x.Reading.Id,
                        ServiceId = x.Reading.ServiceId,
                        ServiceName = x.Reading.Service!.Name,
                        Unit = x.Reading.Service.Unit,
                        ReadOn = x.Reading.ReadOn,
                        ReadingMonth = x.Reading.ReadingMonth.FirstDay,
                        PreviousValue = x.Reading.PreviousValue,
                        CurrentValue = x.Reading.CurrentValue,
                        Consumption = x.Reading.Consumption,
                        InvoiceNumber = liveUtilityItems
                            .Where(i => i.SourceId == x.Reading.Id)
                            .Select(i => i.Invoice!.InvoiceNumber)
                            .FirstOrDefault()
                    },
                    StorageKey = x.Media == null ? null : x.Media.StorageKey,
                    MimeType = x.Media == null ? null : x.Media.MimeType
                }),
            cancellationToken);

        return new PagedResult<MyUtilityReadingResponse>
        {
            Items = page.Items
                .Select(x => x.Response with
                {
                    ImageUrl = x.StorageKey is null ? null : storageProvider.GetUrl(x.StorageKey, x.MimeType!)
                })
                .ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems
        };
    }

    public Task<PagedResult<MyPaymentResponse>> SearchPaymentsAsync(
        Guid userId, MyPaymentListQuery query, CancellationToken cancellationToken = default)
    {
        var visibleInvoices = VisibleInvoices(userId);
        var payments = context.Payments
            .IgnoreQueryFilters()
            .Where(p => visibleInvoices.Any(i => i.Id == p.InvoiceId));

        if (query.InvoiceId is not null)
        {
            payments = payments.Where(p => p.InvoiceId == query.InvoiceId);
        }

        if (query.Status is not null)
        {
            payments = payments.Where(p => p.Status == query.Status);
        }

        return payments.ToPagedResultAsync(
            query,
            p => p.PaymentDate,
            SortOrder.Desc,
            page => page.Select(p => new MyPaymentResponse
            {
                Id = p.Id,
                OrganizationId = p.Invoice!.OrganizationId,
                OrganizationName = p.Invoice.Organization!.Name,
                PropertyName = p.Invoice.Lease!.Room!.Property!.Name,
                RoomNumber = p.Invoice.Lease.Room.RoomNumber,
                LeaseCode = p.Invoice.Lease.LeaseCode,
                InvoiceId = p.InvoiceId!.Value,
                InvoiceNumber = p.Invoice.InvoiceNumber,
                PayerName = p.PayerNameOverride ?? p.PayerUser!.FullName,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                TransactionReference = p.TransactionReference,
                PaymentDate = p.PaymentDate,
                ConfirmedAt = p.ConfirmedAt,
                VoidedAt = p.VoidedAt
            }),
            cancellationToken);
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
