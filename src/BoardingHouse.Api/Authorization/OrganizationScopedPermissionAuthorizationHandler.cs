using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Authorization;

public class OrganizationScopedPermissionAuthorizationHandler(IOrganizationScopeAccessor organizationScopeAccessor)
    : AuthorizationHandler<OrganizationScopedPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationScopedPermissionRequirement requirement)
    {
        var scope = await organizationScopeAccessor.GetScopeAsync(requirement.Resource, requirement.Action);

        if (scope.IsUnrestricted || scope.OrganizationIds.Count > 0)
        {
            context.Succeed(requirement);
        }
    }
}
