using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class RoomAssetConditionHistoryRepository(AppDbContext context) : IRoomAssetConditionHistoryRepository
{
    public async Task AddAsync(RoomAssetConditionHistory history, CancellationToken cancellationToken = default) =>
        await context.RoomAssetConditionHistories.AddAsync(history, cancellationToken);

    public async Task<List<RoomAssetConditionHistory>> ListByAssetIdAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        await context.RoomAssetConditionHistories
            .Where(h => h.AssetId == assetId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);
}
