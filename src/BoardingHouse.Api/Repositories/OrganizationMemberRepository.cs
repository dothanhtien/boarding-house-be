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

    public Task<List<(Guid OrganizationId, Guid RoleId)>> GetActiveMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Context.OrganizationMembers
            .Where(m => m.UserId == userId && m.Role!.IsActive)
            .Select(m => new ValueTuple<Guid, Guid>(m.OrganizationId, m.RoleId))
            .ToListAsync(cancellationToken);
}
