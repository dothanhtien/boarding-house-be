using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Roles;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(IRoleService roleService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionResources.Role, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<RoleResponse>>>> GetAll(CancellationToken cancellationToken)
    {
        var roles = await roleService.GetAllAsync(cancellationToken);
        return Ok(new ApiResponse<List<RoleResponse>> { Data = roles });
    }
}
