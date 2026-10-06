using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Repositories;

public interface IRoomRepository : IRepository<Room>
{
    Task<PagedResult<EntityWithMedia<Room>>> SearchAsync(
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
    Task<Room?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Room?> GetByIdWithPropertyForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Room?> GetByIdWithPropertyForShareAsync(Guid id, CancellationToken cancellationToken = default);
    Task LoadAmenitiesAsync(Room room, CancellationToken cancellationToken = default);
    Task LoadAssetsAsync(Room room, CancellationToken cancellationToken = default);
    Task LoadMediaAsync(Room room, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default);
    void AddAmenity(Room room, RoomAmenity amenity);
    void AddMedia(Room room, RoomMedia media);
    void RemoveMedia(Room room, RoomMedia media);
}
