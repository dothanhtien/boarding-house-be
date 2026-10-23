using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface ILeaseUtilityServiceRepository
{
    Task<List<LeaseUtilityService>> ListByLeaseIdAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<LeaseUtilityService?> GetAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default);
    Task AddAsync(LeaseUtilityService leaseUtilityService, CancellationToken cancellationToken = default);
    void Remove(LeaseUtilityService leaseUtilityService);
    Task<bool> ExistsByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<bool> ExistsOpenWithoutPriceOverrideByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default);
}
