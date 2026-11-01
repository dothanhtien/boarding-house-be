using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Authorize]
public class UtilityReadingsController(IUtilityReadingService utilityReadingService) : ControllerBase
{
    [HttpPost("api/rooms/{roomId:guid}/utility-readings")]
    [RequireOrganizationScopedPermission(PermissionResources.UtilityReading, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<UtilityReadingRoomMetersResponse>>> Record(
        Guid roomId, RecordRoomUtilityReadingsRequest request, CancellationToken cancellationToken)
    {
        var room = await utilityReadingService.RecordAsync(roomId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ApiResponse<UtilityReadingRoomMetersResponse> { Data = room });
    }
}
