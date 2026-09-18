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
}
