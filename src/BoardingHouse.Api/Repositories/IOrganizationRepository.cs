using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IOrganizationRepository : IRepository<Organization>
{
    Task<Organization?> GetByIdWithMembersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<Organization>> SearchAsync(
        bool? isActive,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Organization, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
}
