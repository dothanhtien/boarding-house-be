using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IRoleRepository : IRepository<Role>
{
    Task<List<(string Resource, string Action)>> GetPermissionsByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);
}
