using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Repositories;

public interface IMediaAssetRepository : IRepository<MediaAsset>
{
    Task<MediaAsset?> GetByEntityAsync(
        MediaAssetEntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);
}
