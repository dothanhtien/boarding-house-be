using Mapster;
using BoardingHouse.Application.DTOs.Roles;
using BoardingHouse.Application.Repositories;

namespace BoardingHouse.Api.Services;

public class RoleService(IRoleRepository roleRepository) : IRoleService
{
    public async Task<List<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roles = await roleRepository.GetAllAsync(cancellationToken);
        return roles.Adapt<List<RoleResponse>>();
    }
}
