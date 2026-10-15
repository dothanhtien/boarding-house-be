using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Leases;

namespace BoardingHouse.Api.Services;

public interface ILeaseService
{
    Task<PagedResult<LeaseResponse>> GetAllAsync(LeaseListQuery query, CancellationToken cancellationToken = default);
    Task<LeaseResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LeaseResponse> CreateAsync(CreateLeaseRequest request, CancellationToken cancellationToken = default);
    Task<LeaseResponse> UpdateAsync(Guid id, UpdateLeaseRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
