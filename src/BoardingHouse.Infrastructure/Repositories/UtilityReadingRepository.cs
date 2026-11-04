using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class UtilityReadingRepository(AppDbContext context) : IUtilityReadingRepository
{
    public Task<UtilityReading?> GetByIdAsync(Guid roomId, Guid readingId, CancellationToken cancellationToken = default) =>
        context.UtilityReadings.FirstOrDefaultAsync(r => r.Id == readingId && r.RoomId == roomId, cancellationToken);

    public Task<bool> ExistsByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        context.UtilityReadings.AnyAsync(r => r.ServiceId == serviceId, cancellationToken);

    public Task<Guid> GetLatestIdOfMeterAsync(Guid roomId, Guid serviceId, CancellationToken cancellationToken = default) =>
        context.UtilityReadings
            .Where(r => r.RoomId == roomId && r.ServiceId == serviceId)
            .OrderByDescending(r => r.ReadOn)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task AddRangeAsync(IEnumerable<UtilityReading> readings, CancellationToken cancellationToken = default) =>
        context.UtilityReadings.AddRangeAsync(readings, cancellationToken);

    public void SoftDelete(UtilityReading reading) => context.UtilityReadings.Remove(reading);

    public Task<List<EntityWithMedia<UtilityReading>>> ListByRoomAndMonthWithImageAsync(Guid roomId, DateOnly readingMonth, CancellationToken cancellationToken = default) =>
        context.UtilityReadings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .Include(r => r.BilledLease)
            .Where(r => r.RoomId == roomId && r.ReadingMonth == readingMonth && r.DeletedAt == null)
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

    public async Task<List<UtilityReading>> ListLastBilledOfMonthByRoomIdsAsync(
        IReadOnlyCollection<Guid> roomIds, DateOnly readingMonth, CancellationToken cancellationToken = default)
    {
        var rows = await context.UtilityReadings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => roomIds.Contains(r.RoomId)
                && r.ReadingMonth == readingMonth
                && r.BilledLeaseId != null
                && r.DeletedAt == null)
            .GroupBy(r => r.RoomId)
            .Select(g => g
                .OrderByDescending(r => r.CreatedAt)
                .ThenBy(r => r.Id)
                .Select(r => new { Reading = r, r.BilledLease })
                .First())
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.Reading.BilledLease = row.BilledLease;
        }

        return rows.Select(row => row.Reading).ToList();
    }
}
