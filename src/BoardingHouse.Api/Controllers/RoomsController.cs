using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/rooms")]
[Authorize]
public class RoomsController(IRoomService roomService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission("room", "read")]
    public async Task<ActionResult<ApiResponse<PagedResult<RoomResponse>>>> GetAll(
        [FromQuery] RoomListQuery query, CancellationToken cancellationToken)
    {
        var rooms = await roomService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<RoomResponse>> { Data = rooms });
    }

    [HttpGet("{id:guid}")]
    [RequireOrganizationScopedPermission("room", "read")]
    public async Task<ActionResult<ApiResponse<RoomResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var room = await roomService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<RoomResponse> { Data = room });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission("room", "create")]
    public async Task<ActionResult<ApiResponse<RoomResponse>>> Create(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var room = await roomService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = room.Id },
            new ApiResponse<RoomResponse> { Data = room });
    }

    [HttpPatch("{id:guid}")]
    [RequireOrganizationScopedPermission("room", "update")]
    public async Task<ActionResult<ApiResponse<RoomResponse>>> Update(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken)
    {
        var room = await roomService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<RoomResponse> { Data = room });
    }

    [HttpDelete("{id:guid}")]
    [RequireOrganizationScopedPermission("room", "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await roomService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
