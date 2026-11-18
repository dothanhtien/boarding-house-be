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

    public Task<List<Guid>> ListOrganizationIdsWithAutoGenerateInvoicesAsync(CancellationToken cancellationToken = default) =>
        context.OrganizationSettings
            .Where(s => s.AutoGenerateInvoices)
            .OrderBy(s => s.OrganizationId)
            .Select(s => s.OrganizationId)
            .ToListAsync(cancellationToken);
}
