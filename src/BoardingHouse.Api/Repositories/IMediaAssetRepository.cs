using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.Repositories;

public interface IMediaAssetRepository : IRepository<MediaAsset>
{
    Task<MediaAsset?> GetByEntityAsync(
        MediaAssetEntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);
}
