using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Authorize]
public class UtilityReadingsController(IUtilityReadingService utilityReadingService) : ControllerBase
{
    [HttpGet("api/properties/{propertyId:guid}/utility-readings/rooms")]
    [RequireOrganizationScopedPermission(PermissionResources.UtilityReading, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<PagedResult<UtilityReadingRoomListItemResponse>>>> GetRooms(
        Guid propertyId, [FromQuery] UtilityReadingRoomListQuery query, CancellationToken cancellationToken)
    {
        var rooms = await utilityReadingService.GetRoomsAsync(propertyId, query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<UtilityReadingRoomListItemResponse>> { Data = rooms });
    }

    [HttpGet("api/rooms/{roomId:guid}/utility-readings/meters")]
    [RequireOrganizationScopedPermission(PermissionResources.UtilityReading, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<UtilityReadingRoomMetersResponse>>> GetRoomMeters(
        Guid roomId, [FromQuery] UtilityReadingRoomMetersQuery query, CancellationToken cancellationToken)
    {
        var room = await utilityReadingService.GetRoomMetersAsync(roomId, query, cancellationToken);
        return Ok(new ApiResponse<UtilityReadingRoomMetersResponse> { Data = room });
    }

    [HttpPost("api/rooms/{roomId:guid}/utility-readings")]
    [RequireOrganizationScopedPermission(PermissionResources.UtilityReading, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<UtilityReadingRoomMetersResponse>>> Record(
        Guid roomId, RecordRoomUtilityReadingsRequest request, CancellationToken cancellationToken)
    {
        var room = await utilityReadingService.RecordAsync(roomId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ApiResponse<UtilityReadingRoomMetersResponse> { Data = room });
    }

    [HttpDelete("api/rooms/{roomId:guid}/utility-readings/{readingId:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.UtilityReading, PermissionActions.Delete)]
    public async Task<IActionResult> Delete(Guid roomId, Guid readingId, CancellationToken cancellationToken)
    {
        await utilityReadingService.DeleteAsync(roomId, readingId, cancellationToken);
        return NoContent();
    }
}
