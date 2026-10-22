using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Caching;

namespace BoardingHouse.Application.Services;

public class PermissionService(
    IUserRoleRepository userRoleRepository,
    IRoleRepository roleRepository,
    IRolePermissionCache rolePermissionCache) : IPermissionService
{
    public async Task<bool> HasPermissionAsync(Guid userId, string resource, string action, CancellationToken cancellationToken = default)
    {
        var roleIds = await userRoleRepository.GetActiveRoleIdsByUserIdAsync(userId, cancellationToken);

        var permissionsByRole = await Task.WhenAll(
            roleIds.Select(roleId => GetRolePermissionsAsync(roleId, cancellationToken)));

        return permissionsByRole.Any(permissions => permissions.Contains((resource, action)));
    }

    public async Task<List<(string Resource, string Action)>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var permissions = await rolePermissionCache.GetAsync(roleId, cancellationToken);

        if (permissions is null)
        {
            permissions = await roleRepository.GetPermissionsByRoleIdAsync(roleId, cancellationToken);
            await rolePermissionCache.SetAsync(roleId, permissions, cancellationToken);
        }

        return permissions;
    }
}
