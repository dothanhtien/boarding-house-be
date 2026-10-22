using BoardingHouse.Application.DTOs.Organizations;

namespace BoardingHouse.Application.Services;

public interface IOrganizationSettingsService
{
    Task<OrganizationSettingsResponse> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationSettingsResponse> UpsertAsync(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken = default);
}
