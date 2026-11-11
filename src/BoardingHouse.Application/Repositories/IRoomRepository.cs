using System.Linq.Expressions;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Repositories;

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
    Task<PagedResult<RoomWithRecordedMeterCount>> SearchForUtilityReadingAsync(
        Guid propertyId,
        BillingMonth readingMonth,
        bool? recorded,
        string? search,
        PageRequest pageRequest,
        Expression<Func<RoomWithRecordedMeterCount, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
    Task<Room?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Room?> GetByIdWithPropertyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EntityWithOrganizationId<Room>?> GetByIdWithOrganizationIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EntityWithOrganizationId<Room>?> GetByIdWithOrganizationIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EntityWithOrganizationId<Room>?> GetAggregateForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EntityWithOrganizationId<Room>?> GetByIdWithOrganizationIdForShareAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default);
}
