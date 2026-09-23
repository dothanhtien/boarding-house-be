using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IPropertyRepository : IRepository<Property>
{
    Task<Property?> GetByIdForShareAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Property?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<Property>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        bool? isActive,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Property, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
}
