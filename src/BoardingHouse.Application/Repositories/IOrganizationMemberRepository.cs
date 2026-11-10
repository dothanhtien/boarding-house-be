using System.Linq.Expressions;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Repositories;

public interface IOrganizationMemberRepository
{
    Task<PagedResult<OrganizationMember>> SearchByOrganizationIdAsync(
        Guid organizationId,
        string? search,
        PageRequest pageRequest,
        Expression<Func<OrganizationMember, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
    Task<List<(Guid OrganizationId, Guid RoleId)>> GetActiveMembershipsByUserIdAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
