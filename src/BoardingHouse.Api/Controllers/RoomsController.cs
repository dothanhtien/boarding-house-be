using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Storage;
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
    public async Task<ActionResult<ApiResponse<RoomDetailsResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var room = await roomService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<RoomDetailsResponse> { Data = room });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission("room", "create")]
    public async Task<ActionResult<ApiResponse<RoomDetailsResponse>>> Create(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var room = await roomService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = room.Id },
            new ApiResponse<RoomDetailsResponse> { Data = room });
    }

    [HttpPatch("{id:guid}")]
    [RequireOrganizationScopedPermission("room", "update")]
    public async Task<ActionResult<ApiResponse<RoomDetailsResponse>>> Update(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken)
    {
        var room = await roomService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<RoomDetailsResponse> { Data = room });
    }

    [HttpDelete("{id:guid}")]
    [RequireOrganizationScopedPermission("room", "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await roomService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequireOrganizationScopedPermission("room", "update")]
    public async Task<ActionResult<ApiResponse<List<RoomMediaResponse>>>> AddMedia(
        Guid id,
        [FromForm] AddRoomMediaRequest request,
        CancellationToken cancellationToken)
    {
        var media = await roomService.AddMediaAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<List<RoomMediaResponse>> { Data = media });
    }

    [HttpPatch("{id:guid}/media")]
    [RequireOrganizationScopedPermission("room", "update")]
    public async Task<ActionResult<ApiResponse<List<RoomMediaResponse>>>> UpdateMedia(
        Guid id,
        UpdateRoomMediaRequest request,
        CancellationToken cancellationToken)
    {
        var media = await roomService.UpdateMediaAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<List<RoomMediaResponse>> { Data = media });
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    [RequireOrganizationScopedPermission("room", "update")]
    public async Task<IActionResult> DeleteMedia(Guid id, Guid mediaId, CancellationToken cancellationToken)
    {
        await roomService.DeleteMediaAsync(id, mediaId, cancellationToken);
        return NoContent();
    }
}
