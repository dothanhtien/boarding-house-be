using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/rooms/{roomId:guid}/assets/{assetId:guid}/condition-histories")]
[Authorize]
public class RoomAssetConditionHistoriesController(
    IRoomAssetConditionHistoryService roomAssetConditionHistoryService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission("room", "read")]
    public async Task<ActionResult<ApiResponse<List<RoomAssetConditionHistoryResponse>>>> GetAll(
        Guid roomId, Guid assetId, CancellationToken cancellationToken)
    {
        var histories = await roomAssetConditionHistoryService.GetAllAsync(roomId, assetId, cancellationToken);
        return Ok(new ApiResponse<List<RoomAssetConditionHistoryResponse>> { Data = histories });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission("room", "update")]
    public async Task<ActionResult<ApiResponse<RoomAssetConditionHistoryResponse>>> Create(
        Guid roomId, Guid assetId, CreateRoomAssetConditionHistoryRequest request, CancellationToken cancellationToken)
    {
        var history = await roomAssetConditionHistoryService.CreateAsync(roomId, assetId, request, cancellationToken);
        return CreatedAtAction(
            nameof(GetAll),
            new { roomId, assetId },
            new ApiResponse<RoomAssetConditionHistoryResponse> { Data = history });
    }
}
