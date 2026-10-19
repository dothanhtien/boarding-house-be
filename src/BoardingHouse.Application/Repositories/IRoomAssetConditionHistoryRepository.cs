using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IRoomAssetConditionHistoryRepository
{
    Task AddAsync(RoomAssetConditionHistory history, CancellationToken cancellationToken = default);
    Task<List<RoomAssetConditionHistory>> ListByAssetIdAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<RoomAssetConditionHistoryEntry?> GetByIdInAssetWithMediaAsync(
        Guid assetId,
        Guid historyId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, RoomAssetConditionHistoryEntry>> ListCurrentPhotosByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task<List<MediaAsset>> RemoveMediaByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    void AddMedia(RoomAssetConditionHistory history, RoomAssetConditionHistoryMedia media);
    void RemoveMedia(RoomAssetConditionHistory history, RoomAssetConditionHistoryMedia media);
    void MarkUpdated(RoomAssetConditionHistory history);
}
