using Microsoft.EntityFrameworkCore;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Repositories;

namespace BoardingHouse.Infrastructure.Repositories;

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
