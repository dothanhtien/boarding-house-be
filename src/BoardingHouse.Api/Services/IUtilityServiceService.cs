using BoardingHouse.Api.DTOs.UtilityServices;

namespace BoardingHouse.Api.Services;

public interface IUtilityServiceService
{
    Task<List<UtilityServiceResponse>> GetAllAsync(UtilityServiceListQuery query, CancellationToken cancellationToken = default);
    Task<UtilityServiceResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UtilityServiceResponse> CreateAsync(CreateUtilityServiceRequest request, CancellationToken cancellationToken = default);
    Task<UtilityServiceResponse> UpdateAsync(Guid id, UpdateUtilityServiceRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
