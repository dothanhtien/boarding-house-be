using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class RoomAssetConditionHistory : Entity, IUpdateAuditable
{
    public const int MaxMedia = 10;

    public Guid AssetId { get; private set; }
    public RoomAsset? Asset { get; private set; }

    public AssetCondition? OldCondition { get; private set; }
    public AssetCondition NewCondition { get; private set; }
    public string? Note { get; private set; }
    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    private readonly List<RoomAssetConditionHistoryMedia> _media = [];

    public IReadOnlyCollection<RoomAssetConditionHistoryMedia> Media => _media;

    private RoomAssetConditionHistory() { }

    public RoomAssetConditionHistory(
        Guid assetId,
        AssetCondition? oldCondition,
        AssetCondition newCondition,
        string? note,
        IReadOnlyList<MediaAsset> photos,
        Guid changedBy,
        DateTimeOffset now)
    {
        EnsureWithinMediaLimit(photos.Count);

        AssetId = assetId;
        OldCondition = oldCondition;
        NewCondition = newCondition;
        Note = note;
        ChangedBy = changedBy;
        ChangedAt = now;

        AddPhotos(photos, changedBy);
    }

    public IReadOnlyList<MediaAsset> Update(
        Change<string?> note,
        IReadOnlyCollection<Guid> removeMediaIds,
        IReadOnlyList<MediaAsset> addedPhotos,
        Guid actor,
        DateTimeOffset now)
    {
        EnsureCanUpdatePhotos(removeMediaIds, addedPhotos.Count);

        if (note.IsSet)
        {
            Note = note.Value;
        }

        var removed = _media.Where(m => removeMediaIds.Contains(m.MediaAssetId)).ToList();
        foreach (var media in removed)
        {
            _media.Remove(media);
        }

        AddPhotos(addedPhotos, actor);

        if (removed.Count > 0 || addedPhotos.Count > 0)
        {
            UpdatedAt = now;
            UpdatedBy = actor;
        }

        return removed.Select(m => m.MediaAsset!).ToList();
    }

    // When the asset is deleted: gives back every photo so the caller can remove the files.
    public IReadOnlyList<MediaAsset> RemoveAllPhotos()
    {
        var removed = _media.Select(m => m.MediaAsset!).ToList();
        _media.Clear();
        return removed;
    }

    public void EnsureCanUpdatePhotos(IReadOnlyCollection<Guid> removeMediaIds, int addedCount)
    {
        var currentIds = _media.Select(m => m.MediaAssetId).ToHashSet();
        var unknown = removeMediaIds.Where(id => !currentIds.Contains(id)).Cast<Guid?>().FirstOrDefault();
        if (unknown is not null)
        {
            throw DomainException.NotFound($"Room asset condition photo '{unknown}' not found");
        }

        if (addedCount > 0)
        {
            EnsureWithinMediaLimit(currentIds.Count - removeMediaIds.Count + addedCount);
        }
    }

    private static void EnsureWithinMediaLimit(int count)
    {
        if (count > MaxMedia)
        {
            throw new DomainException($"A condition entry must not have more than {MaxMedia} photos");
        }
    }

    private void AddPhotos(IReadOnlyList<MediaAsset> photos, Guid createdBy)
    {
        var nextSortOrder = _media.Select(m => m.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        for (var i = 0; i < photos.Count; i++)
        {
            _media.Add(new RoomAssetConditionHistoryMedia(Id, photos[i], nextSortOrder + i, createdBy));
        }
    }
}
