using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface ILeaseUtilityServiceRepository
{
    Task<List<LeaseUtilityService>> ListByLeaseIdAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<List<LeaseUtilityService>> ListByLeaseIdsIncludingInactiveAsync(IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken = default);
    Task<LeaseUtilityService?> GetAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default);
    Task AddAsync(LeaseUtilityService leaseUtilityService, CancellationToken cancellationToken = default);
    void Remove(LeaseUtilityService leaseUtilityService);
}
