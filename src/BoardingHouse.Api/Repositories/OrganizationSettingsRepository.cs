using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class OrganizationSettingsRepository(AppDbContext context) : IOrganizationSettingsRepository
{
    public Task<OrganizationSettings?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        context.OrganizationSettings.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

    public Task AddAsync(OrganizationSettings settings, CancellationToken cancellationToken = default) =>
        context.OrganizationSettings.AddAsync(settings, cancellationToken).AsTask();

    public void Update(OrganizationSettings settings) => context.OrganizationSettings.Update(settings);
}
