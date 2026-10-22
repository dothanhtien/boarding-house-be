using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class TenantProfileRepository(AppDbContext context) : Repository<TenantProfile>(context), ITenantProfileRepository
{
    public Task<TenantProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Context.TenantProfiles.FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

    public Task<bool> ExistsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Context.TenantProfiles.AnyAsync(t => t.UserId == userId, cancellationToken);
}
