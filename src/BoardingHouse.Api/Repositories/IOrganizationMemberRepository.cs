using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IOrganizationMemberRepository : IRepository<OrganizationMember>
{
    Task<OrganizationMember?> GetByOrganizationAndUserIdAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<OrganizationMember?> GetByIdWithUserAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<List<OrganizationMember>> GetByOrganizationAndRoleIdAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrganizationMember>> SearchByOrganizationIdAsync(
        Guid organizationId,
        string? search,
        PageRequest pageRequest,
        Expression<Func<OrganizationMember, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
}
