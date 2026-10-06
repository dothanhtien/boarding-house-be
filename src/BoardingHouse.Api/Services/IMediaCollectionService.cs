using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Services;

public interface IMediaCollectionService
{
    Task<IReadOnlyList<MediaAsset>> UploadAsync(
        MediaAssetEntityType entityType,
        IReadOnlyList<IFormFile> files,
        string folder,
        Guid? organizationId,
        CancellationToken cancellationToken = default);
    Task StageAddAsync(IEnumerable<MediaAsset> uploaded, CancellationToken cancellationToken = default);
    void StageRemove(IEnumerable<MediaAsset> assets);
    Task CompleteAsync(IEnumerable<MediaAsset> removed);
    Task DiscardAsync(IEnumerable<MediaAsset> uploaded);
}
