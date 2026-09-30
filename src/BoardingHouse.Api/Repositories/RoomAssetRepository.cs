using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class RoomAssetRepository(AppDbContext context) : Repository<RoomAsset>(context), IRoomAssetRepository
{
    public Task<RoomAsset?> GetByIdWithRoomAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default) =>
        Context.RoomAssets
            .Include(a => a.Room!.Property)
            .FirstOrDefaultAsync(a => a.Id == assetId && a.RoomId == roomId, cancellationToken);

    public Task<RoomAsset?> GetByIdWithRoomForUpdateAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default) =>
        Context.RoomAssets
            .FromSql($"SELECT * FROM room_assets WHERE id = {assetId} AND room_id = {roomId} FOR UPDATE")
            .Include(a => a.Room!.Property)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<RoomAsset>> ListByRoomIdForUpdateAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        Context.RoomAssets
            .FromSql($"SELECT * FROM room_assets WHERE room_id = {roomId} FOR UPDATE")
            .ToListAsync(cancellationToken);
}
