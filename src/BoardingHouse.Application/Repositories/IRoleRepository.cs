using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IRoleRepository : IRepository<Role>
{
    Task<List<(string Resource, string Action)>> GetPermissionsByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);
    Task<Role?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
