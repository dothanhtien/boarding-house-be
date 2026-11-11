using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class RoomAssetConditionHistoryRepository(AppDbContext context) : IRoomAssetConditionHistoryRepository
{
    public async Task AddAsync(RoomAssetConditionHistory history, CancellationToken cancellationToken = default) =>
        await context.RoomAssetConditionHistories.AddAsync(history, cancellationToken);

    public async Task<List<RoomAssetConditionHistory>> ListByAssetIdAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        await context.RoomAssetConditionHistories
            .AsNoTracking()
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Include(h => h.Media).ThenInclude(m => m.MediaAsset)
            .Where(h => h.AssetId == assetId)
            .ToListAsync(cancellationToken);

    public async Task<RoomAssetConditionHistoryEntry?> GetByIdInAssetWithMediaAsync(
        Guid assetId, Guid historyId, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var histories = asNoTracking
            ? context.RoomAssetConditionHistories.AsNoTracking()
            : context.RoomAssetConditionHistories;

        return await WithIsLatest(histories
                .Include(h => h.Media).ThenInclude(m => m.MediaAsset)
                .Where(h => h.Id == historyId && h.AssetId == assetId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, RoomAssetConditionHistoryEntry>> ListCurrentPhotosByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return new Dictionary<Guid, RoomAssetConditionHistoryEntry>();
        }

        var current = await WithIsLatest(Latest(context.RoomAssetConditionHistories
                    .Where(h => assetIds.Contains(h.AssetId) && h.Media.Any()))
                .AsNoTracking()
                .Include(h => h.Media).ThenInclude(m => m.MediaAsset))
            .ToListAsync(cancellationToken);

        return current.ToDictionary(x => x.History.AssetId);
    }

    public async Task<List<RoomAssetConditionHistory>> ListWithPhotosByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return [];
        }

        return await context.RoomAssetConditionHistories
            .Include(h => h.Media).ThenInclude(m => m.MediaAsset)
            .Where(h => assetIds.Contains(h.AssetId) && h.Media.Any())
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RoomAssetConditionHistory> Latest(IQueryable<RoomAssetConditionHistory> source) =>
        source.Where(h => !source.Any(n =>
            n.AssetId == h.AssetId
            && (n.ChangedAt > h.ChangedAt || (n.ChangedAt == h.ChangedAt && n.Id.CompareTo(h.Id) > 0))));

    private IQueryable<RoomAssetConditionHistoryEntry> WithIsLatest(IQueryable<RoomAssetConditionHistory> source)
    {
        var all = context.RoomAssetConditionHistories;
        return source.Select(h => new RoomAssetConditionHistoryEntry(h, !all.Any(n =>
            n.AssetId == h.AssetId
            && (n.ChangedAt > h.ChangedAt || (n.ChangedAt == h.ChangedAt && n.Id.CompareTo(h.Id) > 0)))));
    }
}
