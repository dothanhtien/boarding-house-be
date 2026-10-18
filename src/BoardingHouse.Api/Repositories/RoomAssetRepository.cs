using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Repositories;

public class RoomAssetRepository(AppDbContext context) : Repository<RoomAsset>(context), IRoomAssetRepository
{
    public Task<Guid?> GetOrganizationIdAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default) =>
        Context.RoomAssets
            .Where(a => a.Id == assetId && a.RoomId == roomId)
            .Select(a => (Guid?)a.Room!.Property!.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityWithOrganizationId<RoomAsset>?> GetByIdWithOrganizationIdForUpdateAsync(
        Guid roomId, Guid assetId, CancellationToken cancellationToken = default) =>
        Context.RoomAssets
            .FromSql($"SELECT * FROM room_assets WHERE id = {assetId} AND room_id = {roomId} FOR UPDATE")
            .Select(a => new EntityWithOrganizationId<RoomAsset>(a, a.Room!.Property!.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<RoomAsset>> ListByRoomIdForUpdateAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        Context.RoomAssets
            .FromSql($"SELECT * FROM room_assets WHERE room_id = {roomId} FOR UPDATE")
            .ToListAsync(cancellationToken);
}
