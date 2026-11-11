using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class RoomRepository(AppDbContext context) : Repository<Room>(context), IRoomRepository
{
    public async Task<PagedResult<EntityWithMedia<Room>>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        RoomCategory? roomCategory,
        RoomStatus? roomStatus,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Room, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var rooms = Context.Rooms.Include(r => r.Amenities).AsQueryable();

        if (organizationId is not null)
        {
            rooms = rooms.Where(r => r.Property!.OrganizationId == organizationId);
        }
        else if (allowedOrganizationIds is not null)
        {
            rooms = rooms.Where(r => allowedOrganizationIds.Contains(r.Property!.OrganizationId));
        }

        if (propertyId is not null)
        {
            rooms = rooms.Where(r => r.PropertyId == propertyId);
        }

        if (roomCategory is not null)
        {
            rooms = rooms.Where(r => r.RoomCategory == roomCategory);
        }

        if (roomStatus is not null)
        {
            rooms = rooms.Where(r => r.RoomStatus == roomStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            rooms = rooms.Where(r => EF.Functions.ILike(r.RoomNumber, pattern, LikePattern.EscapeCharacter));
        }

        return await rooms.ToPagedResultAsync(
            pageRequest,
            sortField,
            sortOrder,
            page => page.LeftJoin(
                Context.RoomMedia.Where(m => m.IsCover),
                r => r.Id,
                m => m.RoomId,
                (r, m) => new EntityWithMedia<Room>(
                    r,
                    m == null ? null : new MediaLocation(m.MediaAsset!.StorageKey, m.MediaAsset.MimeType))),
            cancellationToken);
    }

    public async Task<PagedResult<RoomWithRecordedMeterCount>> SearchForUtilityReadingAsync(
        Guid propertyId,
        BillingMonth readingMonth,
        bool? recorded,
        string? search,
        PageRequest pageRequest,
        Expression<Func<RoomWithRecordedMeterCount, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var rooms = Context.Rooms.Where(r => r.PropertyId == propertyId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            rooms = rooms.Where(r => EF.Functions.ILike(r.RoomNumber, pattern, LikePattern.EscapeCharacter));
        }

        var meteredServices = Context.UtilityServices.Where(s => s.PropertyId == propertyId
            && s.IsActive
            && s.BillingMethod == UtilityBillingMethod.Metered);
        var meterCount = await meteredServices.CountAsync(cancellationToken);

        var recordedCounts = Context.UtilityReadings
            .Where(u => u.ReadingMonth == readingMonth && u.Room!.PropertyId == propertyId)
            .LeftJoin(meteredServices, u => u.ServiceId, s => s.Id, (u, s) => new { u.RoomId, IsMetered = s != null })
            .GroupBy(u => u.RoomId)
            .Select(g => new
            {
                RoomId = g.Key,
                Count = g.Count(),
                MeteredCount = g.Count(u => u.IsMetered)
            });

        var counted = rooms.LeftJoin(
            recordedCounts,
            r => r.Id,
            c => c.RoomId,
            (r, c) => new RoomWithRecordedMeterCount
            {
                Room = r,
                MeterCount = meterCount + ((int?)c!.Count ?? 0) - ((int?)c.MeteredCount ?? 0),
                RecordedCount = (int?)c.Count ?? 0
            });

        if (recorded is bool isRecorded)
        {
            counted = counted.Where(r => (r.RecordedCount == r.MeterCount) == isRecorded);
        }

        return await counted.ToPagedResultAsync(
            pageRequest, sortField, sortOrder, x => x.Room.Id, page => page, cancellationToken);
    }

    public Task<Room?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .AsNoTracking()
            .Include(r => r.Property)
            .Include(r => r.Amenities)
            .Include(r => r.Media).ThenInclude(m => m.MediaAsset)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Room?> GetByIdWithPropertyAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .AsNoTracking()
            .Include(r => r.Property)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<EntityWithOrganizationId<Room>?> GetByIdWithOrganizationIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new EntityWithOrganizationId<Room>(r, r.Property!.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityWithOrganizationId<Room>?> GetByIdWithOrganizationIdForUpdateAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .FromSql($"SELECT * FROM rooms WHERE id = {id} FOR UPDATE")
            .Select(r => new EntityWithOrganizationId<Room>(r, r.Property!.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityWithOrganizationId<Room>?> GetByIdWithOrganizationIdForShareAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .FromSql($"SELECT * FROM rooms WHERE id = {id} FOR SHARE")
            .Select(r => new EntityWithOrganizationId<Room>(r, r.Property!.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<EntityWithOrganizationId<Room>?> GetAggregateForUpdateAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var locked = await GetByIdWithOrganizationIdForUpdateAsync(id, cancellationToken);
        if (locked is null)
        {
            return null;
        }

        var room = locked.Entity;

        await Context.Entry(room).Collection(r => r.Amenities).LoadAsync(cancellationToken);
        await Context.Entry(room).Collection(r => r.Media).Query().Include(m => m.MediaAsset).LoadAsync(cancellationToken);

        return locked;
    }

    public Task<bool> ExistsByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default) =>
        Context.Rooms.AnyAsync(r => r.PropertyId == propertyId, cancellationToken);
}
