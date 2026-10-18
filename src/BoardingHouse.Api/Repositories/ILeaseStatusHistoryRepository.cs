using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Repositories;

public interface ILeaseStatusHistoryRepository
{
    Task AddAsync(LeaseStatusHistory history, CancellationToken cancellationToken = default);
    Task<List<LeaseStatusHistory>> ListByLeaseIdAsync(Guid leaseId, CancellationToken cancellationToken = default);
}
