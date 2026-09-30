using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

namespace BoardingHouse.Api.Services;

public interface IRoomAssetConditionHistoryService
{
    Task<List<RoomAssetConditionHistoryResponse>> GetAllAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<RoomAssetConditionHistoryResponse> CreateAsync(
        Guid roomId,
        Guid assetId,
        CreateRoomAssetConditionHistoryRequest request,
        CancellationToken cancellationToken = default);
}
