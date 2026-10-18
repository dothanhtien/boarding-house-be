using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.Services;

public interface IMediaCollectionService
{
    Task<TResult> AddAsync<TResult>(
        MediaAssetEntityType entityType,
        IReadOnlyList<IFormFile> files,
        string folder,
        Guid? organizationId,
        Func<IReadOnlyList<MediaAsset>, CancellationToken, Task<TResult>> attachAsync,
        CancellationToken cancellationToken = default);
    void StageRemove(IEnumerable<MediaAsset> assets);
    Task CompleteAsync(IEnumerable<MediaAsset> removed);
}
