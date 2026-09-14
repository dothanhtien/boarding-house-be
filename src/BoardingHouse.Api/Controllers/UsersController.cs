using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    [RequirePermission("user", "read")]
    public async Task<ActionResult<ApiResponse<PagedResult<UserResponse>>>> GetAll(
        [FromQuery] UserListQuery query,
        CancellationToken cancellationToken)
    {
        var users = await userService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<UserResponse>> { Data = users });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("user", "read")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<UserResponse> { Data = user });
    }

    [HttpPost]
    [RequirePermission("user", "create")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await userService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, new ApiResponse<UserResponse> { Data = user });
    }

    [HttpPatch("{id:guid}")]
    [RequirePermission("user", "update")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await userService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<UserResponse> { Data = user });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("user", "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await userService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
