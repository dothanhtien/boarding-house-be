using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.DTOs.Roles;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(IRoleRepository roleRepository) : ControllerBase
{
    [HttpGet]
    [RequirePermission("role", "read")]
    public async Task<ActionResult<List<RoleResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var roles = await roleRepository.GetAllAsync(cancellationToken);
        return Ok(roles.Adapt<List<RoleResponse>>());
    }
}
