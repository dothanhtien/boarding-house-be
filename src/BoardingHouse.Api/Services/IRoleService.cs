using BoardingHouse.Application.DTOs.Roles;

namespace BoardingHouse.Api.Services;

public interface IRoleService
{
    Task<List<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
