using System.Linq.Expressions;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Repositories;

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
