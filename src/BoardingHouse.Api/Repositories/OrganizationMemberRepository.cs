using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class OrganizationMemberRepository(AppDbContext context) : Repository<OrganizationMember>(context), IOrganizationMemberRepository
{
    public Task<OrganizationMember?> GetByOrganizationAndUserIdAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
        Context.OrganizationMembers
            .Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken);

    public Task<OrganizationMember?> GetByIdWithUserAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        Context.OrganizationMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);

    public Task<List<OrganizationMember>> GetByOrganizationAndRoleIdAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default) =>
        Context.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.RoleId == roleId)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<OrganizationMember>> SearchByOrganizationIdAsync(
        Guid organizationId,
        string? search,
        PageRequest pageRequest,
        Expression<Func<OrganizationMember, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var members = Context.OrganizationMembers
            .Include(m => m.User)
            .Include(m => m.Role)
            .Where(m => m.OrganizationId == organizationId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            members = members.Where(m =>
                EF.Functions.ILike(m.User!.Email, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(m.User!.FullName, pattern, LikePattern.EscapeCharacter));
        }

        return await members.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }
}
