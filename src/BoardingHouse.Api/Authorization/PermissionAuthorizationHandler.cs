using BoardingHouse.Api.Common;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Authorization;

public class PermissionAuthorizationHandler(IPermissionService permissionService, ICurrentUserAccessor currentUserAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = currentUserAccessor.User;

        if (user is null) return;

        if (await permissionService.HasPermissionAsync(user.Id, requirement.Resource, requirement.Action))
        {
            context.Succeed(requirement);
        }
    }
}
