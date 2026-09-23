using BoardingHouse.Api.Common;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Caching;

namespace BoardingHouse.Api.Services;

public class OrganizationScopeAccessor(
    ICurrentUserAccessor currentUserAccessor,
    IPermissionService permissionService,
    IOrganizationMemberRepository organizationMemberRepository,
    IOrganizationMembershipCache organizationMembershipCache) : IOrganizationScopeAccessor
{
    private readonly Dictionary<(string Resource, string Action), Task<OrganizationScope>> _scopeCache = [];

    public Task<OrganizationScope> GetScopeAsync(string resource, string action, CancellationToken cancellationToken = default)
    {
        var key = (resource, action);
        if (_scopeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var scope = ComputeScopeAsync(resource, action, cancellationToken);
        _scopeCache[key] = scope;
        return scope;
    }

    private async Task<OrganizationScope> ComputeScopeAsync(string resource, string action, CancellationToken cancellationToken)
    {
        var user = currentUserAccessor.User;
        if (user is null) return OrganizationScope.None;

        if (await permissionService.HasPermissionAsync(user.Id, resource, action, cancellationToken))
        {
            return new OrganizationScope { IsUnrestricted = true, OrganizationIds = new HashSet<Guid>() };
        }

        var memberships = await GetMembershipsAsync(user.Id, cancellationToken);
        if (memberships.Count == 0) return OrganizationScope.None;

        var accessibleOrganizationIds = new HashSet<Guid>();
        foreach (var membership in memberships)
        {
            var permissions = await permissionService.GetRolePermissionsAsync(membership.RoleId, cancellationToken);
            if (permissions.Contains((resource, action)))
            {
                accessibleOrganizationIds.Add(membership.OrganizationId);
            }
        }

        return new OrganizationScope { IsUnrestricted = false, OrganizationIds = accessibleOrganizationIds };
    }

    private async Task<List<(Guid OrganizationId, Guid RoleId)>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await organizationMembershipCache.GetAsync(userId, cancellationToken);

        if (memberships is null)
        {
            memberships = await organizationMemberRepository.GetActiveMembershipsByUserIdAsync(userId, cancellationToken);
            await organizationMembershipCache.SetAsync(userId, memberships, cancellationToken);
        }

        return memberships;
    }
}
