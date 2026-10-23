using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Infrastructure.Repositories;

public class LeaseUtilityServiceRepository(AppDbContext context) : ILeaseUtilityServiceRepository
{
    public Task<List<LeaseUtilityService>> ListByLeaseIdAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        context.LeaseUtilityServices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Service)
            .Where(s => s.LeaseId == leaseId)
            .OrderBy(s => s.Service!.Name)
            .ThenBy(s => s.ServiceId)
            .ToListAsync(cancellationToken);

    public Task<LeaseUtilityService?> GetAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default) =>
        context.LeaseUtilityServices
            .FirstOrDefaultAsync(s => s.LeaseId == leaseId && s.ServiceId == serviceId, cancellationToken);

    public async Task AddAsync(LeaseUtilityService leaseUtilityService, CancellationToken cancellationToken = default) =>
        await context.LeaseUtilityServices.AddAsync(leaseUtilityService, cancellationToken);

    public void Remove(LeaseUtilityService leaseUtilityService) =>
        context.LeaseUtilityServices.Remove(leaseUtilityService);

    public Task<bool> ExistsByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        context.LeaseUtilityServices.AnyAsync(
            s => s.ServiceId == serviceId && s.Lease!.DeletedAt == null,
            cancellationToken);

    public Task<bool> ExistsOpenWithoutPriceOverrideByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        context.LeaseUtilityServices.AnyAsync(
            s => s.ServiceId == serviceId
                && s.UnitPriceOverride == null
                && s.Lease!.DeletedAt == null
                && (s.Lease.Status == LeaseStatus.Draft || s.Lease.Status == LeaseStatus.Active),
            cancellationToken);
}
