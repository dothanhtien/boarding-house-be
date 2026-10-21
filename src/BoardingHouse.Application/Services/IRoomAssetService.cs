using BoardingHouse.Application.DTOs.RoomAssets;
using BoardingHouse.Application.DTOs.Rooms;

namespace BoardingHouse.Application.Services;

public interface IRoomAssetService
{
    Task<RoomDetailsAssetResponse> CreateAsync(
        Guid roomId,
        CreateRoomAssetInput input,
        CancellationToken cancellationToken = default);
    Task<RoomAssetResponse> UpdateAsync(
        Guid roomId,
        Guid assetId,
        UpdateRoomAssetRequest request,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<SplitRoomAssetResponse> SplitAsync(
        Guid roomId,
        Guid assetId,
        SplitRoomAssetRequest request,
        CancellationToken cancellationToken = default);
}
