using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class UtilityReadingRepository(AppDbContext context) : IUtilityReadingRepository
{
    public Task AddRangeAsync(IEnumerable<UtilityReading> readings, CancellationToken cancellationToken = default) =>
        context.UtilityReadings.AddRangeAsync(readings, cancellationToken);

    // IgnoreQueryFilters keeps readings of a utility service or billed lease deleted afterwards; it covers the whole
    // query, so the soft-delete filter on media assets is written out
    public Task<List<EntityWithMedia<UtilityReading>>> ListByRoomAndMonthWithImageAsync(Guid roomId, DateOnly readingMonth, CancellationToken cancellationToken = default) =>
        context.UtilityReadings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .Include(r => r.BilledLease)
            .Where(r => r.RoomId == roomId && r.ReadingMonth == readingMonth)
            .LeftJoin(
                context.MediaAssets.Where(m => m.EntityType == MediaAssetEntityType.UtilityReadingImage && m.DeletedAt == null),
                r => r.Id,
                m => m.EntityId,
                (r, m) => new EntityWithMedia<UtilityReading>(r, m == null ? null : new MediaLocation(m.StorageKey, m.MimeType)))
            .ToListAsync(cancellationToken);

    public Task<List<UtilityReading>> ListLatestByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        context.UtilityReadings
            .AsNoTracking()
            .Where(r => r.RoomId == roomId)
            .GroupBy(r => r.ServiceId)
            .Select(g => g.OrderByDescending(r => r.ReadOn).ThenByDescending(r => r.CreatedAt).First())
            .ToListAsync(cancellationToken);
}
