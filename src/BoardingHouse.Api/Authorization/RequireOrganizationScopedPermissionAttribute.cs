using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Authorization;

public class RequireOrganizationScopedPermissionAttribute(string resource, string action)
    : AuthorizeAttribute(policy: $"organization-scoped:{resource}:{action}");
