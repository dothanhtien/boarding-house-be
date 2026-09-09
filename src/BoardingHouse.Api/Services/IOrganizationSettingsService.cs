using BoardingHouse.Api.DTOs.Organizations;

namespace BoardingHouse.Api.Services;

public interface IOrganizationSettingsService
{
    Task<OrganizationSettingsResponse> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationSettingsResponse> UpsertAsync(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken = default);
}
