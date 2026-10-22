using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/rooms/{roomId:guid}/assets/{assetId:guid}/condition-histories")]
[Authorize]
public class RoomAssetConditionHistoriesController(
    IRoomAssetConditionHistoryService roomAssetConditionHistoryService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<RoomAssetConditionHistoryResponse>>>> GetAll(
        Guid roomId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var histories = await roomAssetConditionHistoryService.GetAllAsync(roomId, assetId, cancellationToken);
        return Ok(new ApiResponse<List<RoomAssetConditionHistoryResponse>> { Data = histories });
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<RoomAssetConditionHistoryResponse>>> Create(
        Guid roomId,
        Guid assetId,
        [FromForm] CreateRoomAssetConditionHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var input = new CreateRoomAssetConditionHistoryInput
        {
            NewCondition = request.NewCondition!.Value,
            Note = request.Note,
            Files = request.Files.ToFileUploads()
        };
        var history = await roomAssetConditionHistoryService.CreateAsync(roomId, assetId, input, cancellationToken);
        return CreatedAtAction(
            nameof(GetAll),
            new { roomId, assetId },
            new ApiResponse<RoomAssetConditionHistoryResponse> { Data = history });
    }

    [HttpPatch("{historyId:guid}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaLimits.MaxMultiFileRequestBodyBytes)]
    [RequireOrganizationScopedPermission(PermissionResources.Room, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<RoomAssetConditionHistoryResponse>>> Update(
        Guid roomId,
        Guid assetId,
        Guid historyId,
        [FromForm] UpdateRoomAssetConditionHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var input = new UpdateRoomAssetConditionHistoryInput
        {
            Note = request.Note,
            RemoveMediaIds = request.RemoveMediaIds ?? [],
            Files = request.Files.ToFileUploads()
        };
        var history = await roomAssetConditionHistoryService.UpdateAsync(roomId, assetId, historyId, input, cancellationToken);
        return Ok(new ApiResponse<RoomAssetConditionHistoryResponse> { Data = history });
    }
}
