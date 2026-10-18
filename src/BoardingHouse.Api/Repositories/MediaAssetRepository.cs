using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.Repositories;

public class MediaAssetRepository(AppDbContext context) : Repository<MediaAsset>(context), IMediaAssetRepository
{
    public async Task<MediaAsset?> GetByEntityAsync(
        MediaAssetEntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        return await Context.MediaAssets
            .FirstOrDefaultAsync(m => m.EntityType == entityType && m.EntityId == entityId, cancellationToken);
    }
}
