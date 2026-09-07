using BoardingHouse.Api.DTOs.Organizations;

namespace BoardingHouse.Api.Services;

public interface IOrganizationService
{
    Task<List<OrganizationResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OrganizationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default);
    Task<OrganizationResponse> UpdateAsync(
        Guid id,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
