using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Repositories;

public class LeaseStatusHistoryRepository(AppDbContext context) : ILeaseStatusHistoryRepository
{
    public async Task AddAsync(LeaseStatusHistory history, CancellationToken cancellationToken = default) =>
        await context.LeaseStatusHistories.AddAsync(history, cancellationToken);

    public Task<List<LeaseStatusHistory>> ListByLeaseIdAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        context.LeaseStatusHistories
            .AsNoTracking()
            .Where(h => h.LeaseId == leaseId)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);
}
