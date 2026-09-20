using BoardingHouse.Api.DTOs.Roles;
using BoardingHouse.Api.Repositories;
using Mapster;

namespace BoardingHouse.Api.Services;

public class RoleService(IRoleRepository roleRepository) : IRoleService
{
    public async Task<List<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roles = await roleRepository.GetAllAsync(cancellationToken);
        return roles.Adapt<List<RoleResponse>>();
    }
}
