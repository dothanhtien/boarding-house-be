using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/rooms/{roomId:guid}/assets")]
[Authorize]
public class RoomAssetsController(IRoomAssetService roomAssetService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<RoomDetailsAssetResponse>>> Create(
        Guid roomId, [FromForm] CreateRoomAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await roomAssetService.CreateAsync(roomId, request, cancellationToken);
        return CreatedAtAction(
            nameof(RoomsController.GetById),
            "Rooms",
            new { id = roomId },
            new ApiResponse<RoomDetailsAssetResponse> { Data = asset });
    }

    [HttpPatch("{assetId:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<RoomAssetResponse>>> Update(
        Guid roomId, Guid assetId, UpdateRoomAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await roomAssetService.UpdateAsync(roomId, assetId, request, cancellationToken);
        return Ok(new ApiResponse<RoomAssetResponse> { Data = asset });
    }

    [HttpDelete("{assetId:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Update)]
    public async Task<IActionResult> Delete(Guid roomId, Guid assetId, CancellationToken cancellationToken)
    {
        await roomAssetService.DeleteAsync(roomId, assetId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{assetId:guid}/split")]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<SplitRoomAssetResponse>>> Split(
        Guid roomId, Guid assetId, SplitRoomAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await roomAssetService.SplitAsync(roomId, assetId, request, cancellationToken);
        return CreatedAtAction(
            nameof(RoomsController.GetById),
            "Rooms",
            new { id = roomId },
            new ApiResponse<SplitRoomAssetResponse> { Data = result });
    }
}
