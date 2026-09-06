using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Authorization;

public class RequirePermissionAttribute(string resource, string action)
    : AuthorizeAttribute(policy: $"{resource}:{action}");
