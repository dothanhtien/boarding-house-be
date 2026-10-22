using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface ITenantProfileRepository : IRepository<TenantProfile>
{
    Task<TenantProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
