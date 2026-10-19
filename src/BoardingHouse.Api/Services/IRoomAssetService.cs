using BoardingHouse.Application.DTOs.RoomAssets;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Api.DTOs.RoomAssets;

namespace BoardingHouse.Api.Services;

public interface IRoomAssetService
{
    Task<RoomDetailsAssetResponse> CreateAsync(
        Guid roomId,
        CreateRoomAssetRequest request,
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
