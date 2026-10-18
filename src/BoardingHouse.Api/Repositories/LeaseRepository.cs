using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

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

    public Task<Lease?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(l => l.DeletedAt == null)
            .Include(l => l.Room)
            .Include(l => l.Tenants.Where(t => t.DeletedAt == null)).ThenInclude(t => t.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<Lease?> GetByIdWithRoomForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .FromSql($"SELECT * FROM leases WHERE id = {id} FOR UPDATE")
            .Include(l => l.Room)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Lease?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Leases
            .FromSql($"SELECT * FROM leases WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

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

    public Task<bool> ExistsOpenByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        Context.Leases.AnyAsync(
            l => l.RoomId == roomId && (l.Status == LeaseStatus.Draft || l.Status == LeaseStatus.Active),
            cancellationToken);

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

    public Task LoadTenantsAsync(Lease lease, CancellationToken cancellationToken = default) =>
        Context.Entry(lease).Collection(l => l.Tenants).LoadAsync(cancellationToken);

    public Task LoadTenantsWithUsersAsync(Lease lease, CancellationToken cancellationToken = default) =>
        Context.Entry(lease).Collection(l => l.Tenants).Query()
            .IgnoreQueryFilters()
            .Where(t => t.DeletedAt == null)
            .Include(t => t.User)
            .LoadAsync(cancellationToken);

    public void AddTenant(Lease lease, LeaseTenant tenant)
    {
        lease.Tenants.Add(tenant);
        Context.LeaseTenants.Add(tenant);
    }
}
