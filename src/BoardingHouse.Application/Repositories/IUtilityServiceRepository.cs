using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Repositories;

public interface IUtilityServiceRepository : IRepository<UtilityService>
{
    Task<UtilityService?> GetByIdForShareAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UtilityService?> GetByIdWithPropertyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EntityWithOrganizationId<UtilityService>?> GetByIdWithOrganizationIdForUpdateAsync(
        Guid id, CancellationToken cancellationToken = default);
    Task<List<UtilityService>> ListByPropertyIdAsync(
        Guid propertyId,
        UtilityType? type,
        bool? isActive,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);
}
