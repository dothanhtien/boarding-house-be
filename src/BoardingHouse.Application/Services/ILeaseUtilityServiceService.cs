using BoardingHouse.Application.DTOs.Leases;

namespace BoardingHouse.Application.Services;

public interface ILeaseUtilityServiceService
{
    Task<List<LeaseUtilityServiceResponse>> GetAllAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<LeaseUtilityServiceResponse> UpsertAsync(
        Guid leaseId, Guid serviceId, UpsertLeaseUtilityServiceRequest request, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default);
}
