using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IRoomAssetRepository : IRepository<RoomAsset>
{
    Task<Guid?> GetOrganizationIdAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<EntityWithOrganizationId<RoomAsset>?> GetByIdWithOrganizationIdForUpdateAsync(
        Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<List<RoomAsset>> ListByRoomIdForUpdateAsync(Guid roomId, CancellationToken cancellationToken = default);
}
