using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IUserRoleRepository
{
    Task<List<Guid>> GetActiveRoleIdsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
