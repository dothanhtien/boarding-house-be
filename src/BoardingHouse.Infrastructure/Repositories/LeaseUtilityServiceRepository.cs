using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence;

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

    public Task<List<LeaseUtilityService>> ListByLeaseIdsIncludingInactiveAsync(
        IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken = default) =>
        context.LeaseUtilityServices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(l => l.Service)
            .Where(l => leaseIds.Contains(l.LeaseId))
            .OrderBy(s => s.Service!.Name)
            .ToListAsync(cancellationToken);

    public Task<LeaseUtilityService?> GetAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default) =>
        context.LeaseUtilityServices
            .FirstOrDefaultAsync(s => s.LeaseId == leaseId && s.ServiceId == serviceId, cancellationToken);

    public async Task AddAsync(LeaseUtilityService leaseUtilityService, CancellationToken cancellationToken = default) =>
        await context.LeaseUtilityServices.AddAsync(leaseUtilityService, cancellationToken);

    public void Remove(LeaseUtilityService leaseUtilityService) =>
        context.LeaseUtilityServices.Remove(leaseUtilityService);
}
