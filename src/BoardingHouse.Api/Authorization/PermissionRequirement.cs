using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Authorization;

public class PermissionRequirement(string resource, string action) : IAuthorizationRequirement
{
    public string Resource { get; } = resource;
    public string Action { get; } = action;
}
