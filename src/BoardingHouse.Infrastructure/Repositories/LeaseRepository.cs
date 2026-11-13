using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class LeaseRepository(AppDbContext context) : Repository<Lease>(context), ILeaseRepository
{
    public async Task<PagedResult<Lease>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        Guid? roomId,
        LeaseStatus? status,
        Guid? tenantUserId,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Lease, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var leases = Context.Leases
            .IgnoreQueryFilters()
            .Where(l => l.DeletedAt == null)
            .Include(l => l.Room)
            .Include(l => l.Tenants.Where(t => t.DeletedAt == null)).ThenInclude(t => t.User)
            .AsSplitQuery();

        if (organizationId is not null)
        {
            leases = leases.Where(l => l.OrganizationId == organizationId);
        }
        else if (allowedOrganizationIds is not null)
        {
            leases = leases.Where(l => allowedOrganizationIds.Contains(l.OrganizationId));
        }

        if (propertyId is not null)
        {
            leases = leases.Where(l => l.Room!.PropertyId == propertyId);
        }

        if (roomId is not null)
        {
            leases = leases.Where(l => l.RoomId == roomId);
        }

        if (status is not null)
        {
            leases = leases.Where(l => l.Status == status);
        }

        if (tenantUserId is not null)
        {
            leases = leases.Where(l => l.Tenants.Any(t => t.UserId == tenantUserId && t.DeletedAt == null));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            leases = leases.Where(l => EF.Functions.ILike(l.LeaseCode, pattern, LikePattern.EscapeCharacter));
        }

        return await leases.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }

    public Task<Lease?> GetActiveOfMonthByRoomIdAsync(
        Guid roomId, BillingMonth month, CancellationToken cancellationToken = default) =>
        ActiveOfMonth(month).FirstOrDefaultAsync(l => l.RoomId == roomId, cancellationToken);

    public Task<List<Lease>> ListActiveOfMonthByRoomIdsAsync(
        IReadOnlyCollection<Guid> roomIds, BillingMonth month, CancellationToken cancellationToken = default) =>
        ActiveOfMonth(month).Where(l => roomIds.Contains(l.RoomId)).ToListAsync(cancellationToken);

    private IQueryable<Lease> ActiveOfMonth(BillingMonth month)
    {
        return Context.Leases
            .AsNoTracking()
            .Where(l => l.Status == LeaseStatus.Active
                && l.StartDate <= month.LastDay
                && (l.EndDate == null || l.EndDate >= month.FirstDay));
    }

    public Task<Lease?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(l => l.DeletedAt == null)
            .Include(l => l.Room)
            .Include(l => l.Tenants.Where(t => t.DeletedAt == null)).ThenInclude(t => t.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<Lease?> GetAggregateForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lease = await Context.Leases
            .FromSql($"SELECT * FROM leases WHERE id = {id} FOR UPDATE")
            .Include(l => l.Room)
            .FirstOrDefaultAsync(cancellationToken);

        if (lease is null)
        {
            return null;
        }

        await Context.Entry(lease).Collection(l => l.Tenants).Query()
            .IgnoreQueryFilters()
            .Where(t => t.DeletedAt == null)
            .Include(t => t.User)
            .LoadAsync(cancellationToken);

        await Context.Entry(lease).Collection(l => l.UtilityServices).Query()
            .Include(s => s.Service)
            .LoadAsync(cancellationToken);

        return lease;
    }

    public Task<Lease?> GetByIdForBillingAsync(Guid id, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var leases = forUpdate
            ? Context.Leases.FromSql($"SELECT * FROM leases WHERE id = {id} FOR UPDATE")
            : Context.Leases.Where(l => l.Id == id);

        return leases
            .AsNoTracking()
            .Include(l => l.Room!).ThenInclude(r => r.Property)
            .Include(l => l.Tenants)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Lease?> GetByIdWithTenantsForDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lease = await Context.Leases
            .FromSql($"SELECT * FROM leases WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        if (lease is null)
        {
            return null;
        }

        await Context.Entry(lease).Collection(l => l.Tenants).LoadAsync(cancellationToken);

        return lease;
    }

    public Task<Lease?> GetByIdForShareAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .FromSql($"SELECT * FROM leases WHERE id = {id} FOR SHARE")
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetRoomIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .Where(l => l.Id == id)
            .Select(l => (Guid?)l.RoomId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetOrganizationIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .Where(l => l.Id == id)
            .Select(l => (Guid?)l.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsActiveByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        Context.Leases.AnyAsync(
            l => l.RoomId == roomId && l.Status == LeaseStatus.Active, cancellationToken);

    public Task<int> CountCurrentTenantsOfActiveLeaseByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        Context.LeaseTenants.CountAsync(
            t => t.Lease!.RoomId == roomId && t.Lease.Status == LeaseStatus.Active && t.LeftDate == null,
            cancellationToken);

    public Task<int> CountTenantsInMonthAsync(Guid leaseId, BillingMonth month, CancellationToken cancellationToken = default)
    {
        return Context.LeaseTenants.CountAsync(
            t => t.LeaseId == leaseId
                && (t.JoinedDate ?? t.Lease!.StartDate) <= month.LastDay
                && (t.LeftDate == null || t.LeftDate >= month.FirstDay),
            cancellationToken);
    }

    public Task<bool> ExistsOpenByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        Context.Leases.AnyAsync(
            l => l.RoomId == roomId && (l.Status == LeaseStatus.Draft || l.Status == LeaseStatus.Active),
            cancellationToken);

    public Task<List<Guid>> ListIdsToExpireAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        Context.Leases
            .Where(l => l.Status == LeaseStatus.Active && l.EndDate != null && l.EndDate < today)
            .OrderBy(l => l.EndDate)
            .ThenBy(l => l.Id)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByPreviousLeaseIdAsync(
        Guid previousLeaseId, Guid? excludingLeaseId = null, CancellationToken cancellationToken = default) =>
        Context.Leases.AnyAsync(l => l.PreviousLeaseId == previousLeaseId && l.Id != excludingLeaseId, cancellationToken);

    public Task<bool> IsInRenewalChainAsync(Guid leaseId, Guid chainStartId, CancellationToken cancellationToken = default) =>
        Context.Database
            .SqlQuery<bool>($"""
                WITH RECURSIVE chain AS (
                    SELECT id, previous_lease_id FROM leases WHERE id = {chainStartId}
                    UNION
                    SELECT l.id, l.previous_lease_id FROM leases l JOIN chain c ON l.id = c.previous_lease_id
                )
                SELECT EXISTS (SELECT 1 FROM chain WHERE id = {leaseId}) AS "Value"
                """)
            .SingleAsync(cancellationToken);

    public Task<bool> ExistsTenantInOrganizationsAsync(
        Guid userId, IReadOnlySet<Guid> organizationIds, CancellationToken cancellationToken = default) =>
        Context.LeaseTenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.UserId == userId
                && t.DeletedAt == null
                && t.Lease!.DeletedAt == null
                && organizationIds.Contains(t.Lease.OrganizationId),
                cancellationToken);

    public Task<List<Lease>> ListByTenantUserIdAsync(
        Guid userId, IReadOnlySet<Guid>? allowedOrganizationIds, CancellationToken cancellationToken = default)
    {
        var leases = Context.Leases
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(l => l.DeletedAt == null && l.Tenants.Any(t => t.UserId == userId && t.DeletedAt == null));

        if (allowedOrganizationIds is not null)
        {
            leases = leases.Where(l => allowedOrganizationIds.Contains(l.OrganizationId));
        }

        return leases
            .Include(l => l.Room)
            .OrderByDescending(l => l.StartDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<LeaseStatusHistory>> ListStatusHistoriesAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        Context.LeaseStatusHistories
            .AsNoTracking()
            .Where(h => h.LeaseId == leaseId)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);

    public Task<List<LeaseUtilityService>> ListUtilityServicesAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        Context.LeaseUtilityServices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Service)
            .Where(s => s.LeaseId == leaseId)
            .OrderBy(s => s.Service!.Name)
            .ThenBy(s => s.ServiceId)
            .ToListAsync(cancellationToken);

    public Task<List<LeaseUtilityService>> ListUtilityServicesIncludingInactiveAsync(IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken = default) =>
        Context.LeaseUtilityServices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(l => l.Service)
            .Where(l => leaseIds.Contains(l.LeaseId))
            .OrderBy(s => s.Service!.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsUtilityServiceUsageAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        Context.LeaseUtilityServices.AnyAsync(
            s => s.ServiceId == serviceId && s.Lease!.DeletedAt == null,
            cancellationToken);

    public Task<bool> ExistsActiveUtilityServiceInOpenLeaseAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        Context.LeaseUtilityServices.AnyAsync(
            s => s.ServiceId == serviceId
                && s.IsActive
                && s.Lease!.DeletedAt == null
                && (s.Lease.Status == LeaseStatus.Draft || s.Lease.Status == LeaseStatus.Active),
            cancellationToken);
}
