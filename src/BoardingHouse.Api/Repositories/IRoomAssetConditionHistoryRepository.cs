using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IRoomAssetConditionHistoryRepository
{
    Task AddAsync(RoomAssetConditionHistory history, CancellationToken cancellationToken = default);
    Task<List<RoomAssetConditionHistory>> ListByAssetIdAsync(Guid assetId, CancellationToken cancellationToken = default);
}
