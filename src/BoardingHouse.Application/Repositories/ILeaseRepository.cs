using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

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
    Task<List<Lease>> ListActiveOfMonthByRoomIdsAsync(
        IReadOnlyCollection<Guid> roomIds, BillingMonth month, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetAggregateForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdForBillingAsync(Guid id, bool forUpdate, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdWithTenantsForDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetByIdForShareAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Lease?> GetActiveOfMonthByRoomIdAsync(Guid roomId, BillingMonth month, CancellationToken cancellationToken = default);
    Task<Guid?> GetRoomIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetOrganizationIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsActiveByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<int> CountCurrentTenantsOfActiveLeaseByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<int> CountTenantsInMonthAsync(Guid leaseId, BillingMonth month, CancellationToken cancellationToken = default);
    Task<bool> ExistsOpenByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<List<Guid>> ListIdsToExpireAsync(DateOnly today, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPreviousLeaseIdAsync(
        Guid previousLeaseId,
        Guid? excludingLeaseId = null,
        CancellationToken cancellationToken = default);
    Task<bool> IsInRenewalChainAsync(Guid leaseId, Guid chainStartId, CancellationToken cancellationToken = default);
    Task<bool> ExistsTenantInOrganizationsAsync(Guid userId, IReadOnlySet<Guid> organizationIds, CancellationToken cancellationToken = default);
    Task<List<Lease>> ListByTenantUserIdAsync(Guid userId, IReadOnlySet<Guid>? allowedOrganizationIds, CancellationToken cancellationToken = default);
    Task<List<LeaseStatusHistory>> ListStatusHistoriesAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<List<LeaseUtilityService>> ListUtilityServicesAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<List<LeaseUtilityService>> ListUtilityServicesIncludingInactiveAsync(
        IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken = default);
    Task<bool> ExistsUtilityServiceUsageAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<bool> ExistsActiveUtilityServiceInOpenLeaseAsync(Guid serviceId, CancellationToken cancellationToken = default);
}
