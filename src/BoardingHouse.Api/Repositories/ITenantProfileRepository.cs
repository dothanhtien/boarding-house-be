using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface ITenantProfileRepository : IRepository<TenantProfile>
{
    Task<TenantProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
