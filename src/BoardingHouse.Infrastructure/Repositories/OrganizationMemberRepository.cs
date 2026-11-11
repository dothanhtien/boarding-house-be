using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class OrganizationMemberRepository(AppDbContext context) : IOrganizationMemberRepository
{
    public async Task<PagedResult<OrganizationMember>> SearchByOrganizationIdAsync(
        Guid organizationId,
        string? search,
        PageRequest pageRequest,
        Expression<Func<OrganizationMember, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var members = context.OrganizationMembers
            .Include(m => m.User)
            .Include(m => m.Role)
            .Where(m => m.OrganizationId == organizationId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            members = members.Where(m =>
                EF.Functions.ILike(m.User!.Email!, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(m.User!.FullName, pattern, LikePattern.EscapeCharacter));
        }

        return await members.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }

    public Task<List<(Guid OrganizationId, Guid RoleId)>> GetActiveMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.OrganizationMembers
            .Where(m => m.UserId == userId && m.Role!.IsActive)
            .Select(m => new ValueTuple<Guid, Guid>(m.OrganizationId, m.RoleId))
            .ToListAsync(cancellationToken);
}
