using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IOrganizationSettingsRepository
{
    Task<OrganizationSettings?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task AddAsync(OrganizationSettings settings, CancellationToken cancellationToken = default);
    void Update(OrganizationSettings settings);
    void Detach(OrganizationSettings settings);
}
