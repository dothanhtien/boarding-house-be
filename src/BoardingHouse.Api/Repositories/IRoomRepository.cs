using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Repositories;

public interface IRoomRepository : IRepository<Room>
{
    Task<Room?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    void AddAmenity(Room room, RoomAmenity amenity);
    Task<bool> ExistsByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default);
    Task<PagedResult<Room>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        RoomCategory? roomCategory,
        RoomStatus? roomStatus,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Room, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
}
