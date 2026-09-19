using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IOrganizationMemberRepository : IRepository<OrganizationMember>
{
    Task<OrganizationMember?> GetByOrganizationAndUserIdAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<List<(Guid OrganizationId, Guid RoleId)>> GetActiveMembershipsByUserIdAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
