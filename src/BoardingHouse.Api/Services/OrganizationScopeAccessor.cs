using BoardingHouse.Api.Common;
using BoardingHouse.Api.Repositories;

namespace BoardingHouse.Api.Services;

public class OrganizationScopeAccessor(
    ICurrentUserAccessor currentUserAccessor,
    IPermissionService permissionService,
    IOrganizationMemberRepository organizationMemberRepository) : IOrganizationScopeAccessor
{
    public async Task<OrganizationScope> GetScopeAsync(string resource, string action, CancellationToken cancellationToken = default)
    {
        var user = currentUserAccessor.User;
        if (user is null) return OrganizationScope.None;

        if (await permissionService.HasPermissionAsync(user.Id, resource, action, cancellationToken))
        {
            return new OrganizationScope { IsUnrestricted = true, OrganizationIds = new HashSet<Guid>() };
        }

        var memberships = await organizationMemberRepository.GetActiveMembershipsByUserIdAsync(user.Id, cancellationToken);
        if (memberships.Count == 0) return OrganizationScope.None;

        var permissionsByMembership = await Task.WhenAll(
            memberships.Select(async m => (m.OrganizationId, Permissions: await permissionService.GetRolePermissionsAsync(m.RoleId, cancellationToken))));

        var accessibleOrganizationIds = new HashSet<Guid>();
        foreach (var (organizationId, permissions) in permissionsByMembership)
        {
            if (permissions.Contains((resource, action)))
            {
                accessibleOrganizationIds.Add(organizationId);
            }
        }

        return new OrganizationScope { IsUnrestricted = false, OrganizationIds = accessibleOrganizationIds };
    }
}
