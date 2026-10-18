using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Repositories;

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

    public async Task<List<MediaAsset>> RemoveMediaByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return [];
        }

        var media = await context.RoomAssetConditionHistoryMedia
            .Include(m => m.MediaAsset)
            .Where(m => assetIds.Contains(m.ConditionHistory!.AssetId))
            .ToListAsync(cancellationToken);

        context.RoomAssetConditionHistoryMedia.RemoveRange(media);
        return media.Select(m => m.MediaAsset!).ToList();
    }

    public void AddMedia(RoomAssetConditionHistory history, RoomAssetConditionHistoryMedia media)
    {
        history.Media.Add(media);
        context.RoomAssetConditionHistoryMedia.Add(media);
    }

    public void RemoveMedia(RoomAssetConditionHistory history, RoomAssetConditionHistoryMedia media)
    {
        history.Media.Remove(media);
        context.RoomAssetConditionHistoryMedia.Remove(media);
    }

    public void MarkUpdated(RoomAssetConditionHistory history) =>
        context.Entry(history).Property(h => h.UpdatedAt).IsModified = true;

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
