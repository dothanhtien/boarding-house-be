using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IUserRoleRepository : IRepository<UserRole>
{
    Task<List<(string Resource, string Action)>> GetPermissionsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
