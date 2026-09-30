using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IRoomAssetRepository : IRepository<RoomAsset>
{
    Task<RoomAsset?> GetByIdWithRoomAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<RoomAsset?> GetByIdWithRoomForUpdateAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<List<RoomAsset>> ListByRoomIdForUpdateAsync(Guid roomId, CancellationToken cancellationToken = default);
}
