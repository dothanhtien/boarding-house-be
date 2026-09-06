using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IUserRoleRepository : IRepository<UserRole>
{
    Task<List<Guid>> GetActiveRoleIdsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
