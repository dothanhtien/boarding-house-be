using System.Linq.Expressions;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Repositories;

public interface ILeaseRepository : IRepository<Lease>
{
    Task<PagedResult<Lease>> SearchAsync(
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
        CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdWithRoomForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdForShareAsync(Guid id, CancellationToken cancellationToken = default);
    Task LoadTenantsAsync(Lease lease, CancellationToken cancellationToken = default);
    Task LoadTenantsWithUsersAsync(Lease lease, CancellationToken cancellationToken = default);
    Task<Guid?> GetRoomIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetOrganizationIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsActiveByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<int> CountCurrentTenantsOfActiveLeaseByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<bool> ExistsOpenByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPreviousLeaseIdAsync(
        Guid previousLeaseId,
        Guid? excludingLeaseId = null,
        CancellationToken cancellationToken = default);
    Task<bool> IsInRenewalChainAsync(Guid leaseId, Guid chainStartId, CancellationToken cancellationToken = default);
    Task<bool> ExistsTenantInOrganizationsAsync(Guid userId, IReadOnlySet<Guid> organizationIds, CancellationToken cancellationToken = default);
    Task<List<Lease>> ListByTenantUserIdAsync(Guid userId, IReadOnlySet<Guid>? allowedOrganizationIds, CancellationToken cancellationToken = default);
    void AddTenant(Lease lease, LeaseTenant tenant);
}
