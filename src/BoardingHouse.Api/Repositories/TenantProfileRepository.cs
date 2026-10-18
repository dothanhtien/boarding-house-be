using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class TenantProfileRepository(AppDbContext context) : Repository<TenantProfile>(context), ITenantProfileRepository
{
    public Task<TenantProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Context.TenantProfiles.FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

    public Task<bool> ExistsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Context.TenantProfiles.AnyAsync(t => t.UserId == userId, cancellationToken);
}
