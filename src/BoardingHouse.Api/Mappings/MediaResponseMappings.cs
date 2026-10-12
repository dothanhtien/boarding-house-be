using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Services.Storage;

namespace BoardingHouse.Api.Mappings;

public static class MediaResponseMappings
{
    public static List<RoomMediaResponse> ToMediaResponses(
        this IEnumerable<RoomMedia> media, IStorageProvider storageProvider) =>
        media
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.SortOrder)
            .Select(m => new RoomMediaResponse
            {
                MediaId = m.MediaAssetId,
                Url = storageProvider.GetUrl(m.MediaAsset!.StorageKey, m.MediaAsset.MimeType),
                MimeType = m.MediaAsset.MimeType,
                FileName = m.MediaAsset.FileName,
                SortOrder = m.SortOrder,
                IsCover = m.IsCover,
                CreatedAt = m.CreatedAt
            })
            .ToList();

    public static List<RoomAssetConditionHistoryMediaResponse> ToMediaResponses(
        this IEnumerable<RoomAssetConditionHistoryMedia> media, IStorageProvider storageProvider) =>
        media
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.SortOrder)
            .Select(m => new RoomAssetConditionHistoryMediaResponse
            {
                MediaId = m.MediaAssetId,
                Url = storageProvider.GetUrl(m.MediaAsset!.StorageKey, m.MediaAsset.MimeType),
                MimeType = m.MediaAsset.MimeType,
                FileName = m.MediaAsset.FileName,
                CreatedAt = m.CreatedAt,
                CreatedBy = m.CreatedBy
            })
            .ToList();

    public static RoomAssetCurrentPhotosResponse ToCurrentPhotosResponse(
        this RoomAssetConditionHistory history, bool isLatest, IStorageProvider storageProvider) => new()
    {
        ConditionHistoryId = history.Id,
        Condition = history.NewCondition,
        ChangedAt = history.ChangedAt,
        IsLatest = isLatest,
        Media = history.Media.ToMediaResponses(storageProvider)
    };
}
