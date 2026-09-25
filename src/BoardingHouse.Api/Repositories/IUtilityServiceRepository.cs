using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Repositories;

public interface IUtilityServiceRepository : IRepository<UtilityService>
{
    Task<UtilityService?> GetByIdWithPropertyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UtilityService?> GetByIdWithPropertyForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<UtilityService>> ListByPropertyIdAsync(
        Guid propertyId,
        UtilityType? type,
        bool? isActive,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);
}
