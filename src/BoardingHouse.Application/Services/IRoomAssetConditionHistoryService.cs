using BoardingHouse.Application.DTOs.RoomAssetConditionHistories;

namespace BoardingHouse.Application.Services;

public interface IRoomAssetConditionHistoryService
{
    Task<List<RoomAssetConditionHistoryResponse>> GetAllAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<RoomAssetConditionHistoryResponse> CreateAsync(
        Guid roomId,
        Guid assetId,
        CreateRoomAssetConditionHistoryInput input,
        CancellationToken cancellationToken = default);
    Task<RoomAssetConditionHistoryResponse> UpdateAsync(
        Guid roomId,
        Guid assetId,
        Guid historyId,
        UpdateRoomAssetConditionHistoryInput input,
        CancellationToken cancellationToken = default);
}
