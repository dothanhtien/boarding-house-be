using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Services;

public interface ILeaseService
{
    Task<PagedResult<LeaseResponse>> GetAllAsync(LeaseListQuery query, CancellationToken cancellationToken = default);
    Task<LeaseResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LeaseResponse> CreateAsync(CreateLeaseRequest request, CancellationToken cancellationToken = default);
    Task<LeaseResponse> UpdateAsync(Guid id, UpdateLeaseRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LeaseResponse> ActivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LeaseResponse> TerminateAsync(Guid id, TerminateLeaseRequest request, CancellationToken cancellationToken = default);
    Task<List<LeaseStatusHistoryResponse>> GetStatusHistoriesAsync(Guid id, CancellationToken cancellationToken = default);
}
