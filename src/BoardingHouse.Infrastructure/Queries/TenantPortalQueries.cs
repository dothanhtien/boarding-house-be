using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Queries;
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
}
