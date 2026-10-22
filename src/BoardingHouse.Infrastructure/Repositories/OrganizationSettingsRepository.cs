using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class OrganizationSettingsRepository(AppDbContext context) : IOrganizationSettingsRepository
{
    public Task<OrganizationSettings?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        context.OrganizationSettings.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

    public Task AddAsync(OrganizationSettings settings, CancellationToken cancellationToken = default) =>
        context.OrganizationSettings.AddAsync(settings, cancellationToken).AsTask();

    public void Update(OrganizationSettings settings) => context.OrganizationSettings.Update(settings);

    public void Detach(OrganizationSettings settings) => context.Entry(settings).State = EntityState.Detached;
}
