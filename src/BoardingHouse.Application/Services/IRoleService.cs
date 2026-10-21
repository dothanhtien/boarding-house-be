using BoardingHouse.Application.DTOs.Roles;

namespace BoardingHouse.Application.Services;

public interface IRoleService
{
    Task<List<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
