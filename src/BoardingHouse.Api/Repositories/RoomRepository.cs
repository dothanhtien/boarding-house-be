using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

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

    public Task<Room?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .AsNoTracking()
            .Include(r => r.Property)
            .Include(r => r.Amenities)
            .Include(r => r.Assets)
            .Include(r => r.Media).ThenInclude(m => m.MediaAsset)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Room?> GetByIdWithPropertyForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .FromSql($"SELECT * FROM rooms WHERE id = {id} FOR UPDATE")
            .Include(r => r.Property)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Room?> GetByIdWithPropertyForShareAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .FromSql($"SELECT * FROM rooms WHERE id = {id} FOR SHARE")
            .Include(r => r.Property)
            .FirstOrDefaultAsync(cancellationToken);

    public Task LoadAmenitiesAsync(Room room, CancellationToken cancellationToken = default) =>
        Context.Entry(room).Collection(r => r.Amenities).LoadAsync(cancellationToken);

    public Task LoadAssetsAsync(Room room, CancellationToken cancellationToken = default) =>
        Context.Entry(room).Collection(r => r.Assets).LoadAsync(cancellationToken);

    public Task LoadMediaAsync(Room room, CancellationToken cancellationToken = default) =>
        Context.Entry(room).Collection(r => r.Media).Query().Include(m => m.MediaAsset).LoadAsync(cancellationToken);

    public Task<bool> ExistsByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default) =>
        Context.Rooms.AnyAsync(r => r.PropertyId == propertyId, cancellationToken);

    public void AddAmenity(Room room, RoomAmenity amenity)
    {
        room.Amenities.Add(amenity);
        Context.RoomAmenities.Add(amenity);
    }

    public void AddMedia(Room room, RoomMedia media)
    {
        room.Media.Add(media);
        Context.RoomMedia.Add(media);
    }

    public void RemoveMedia(Room room, RoomMedia media)
    {
        room.Media.Remove(media);
        Context.RoomMedia.Remove(media);
    }
}
