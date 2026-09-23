using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class RoomRepository(AppDbContext context) : Repository<Room>(context), IRoomRepository
{
    public Task<Room?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Rooms
            .Include(r => r.Property)
            .Include(r => r.Amenities)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    // Entity.Id is pre-populated client-side, so an amenity merely appended to a tracked room's collection
    // would be picked up as an existing row (UPDATE → 0 rows affected). Track it as Added explicitly
    public void AddAmenity(Room room, RoomAmenity amenity)
    {
        room.Amenities.Add(amenity);
        Context.RoomAmenities.Add(amenity);
    }

    public Task<bool> ExistsByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default) =>
        Context.Rooms.AnyAsync(r => r.PropertyId == propertyId, cancellationToken);

    public async Task<PagedResult<Room>> SearchAsync(
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

        return await rooms.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }
}
