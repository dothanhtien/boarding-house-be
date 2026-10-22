using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Services;

public interface IOrganizationService
{
    Task<PagedResult<OrganizationResponse>> GetAllAsync(
        OrganizationListQuery query,
        CancellationToken cancellationToken = default);
    Task<OrganizationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default);
    Task<OrganizationResponse> UpdateAsync(
        Guid id,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
