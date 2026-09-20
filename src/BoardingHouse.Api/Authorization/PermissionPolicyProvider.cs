using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BoardingHouse.Api.Authorization;

public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackPolicyProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        const string organizationScopedPrefix = "organization-scoped:";

        if (policyName.StartsWith(organizationScopedPrefix, StringComparison.Ordinal))
        {
            var scopedParts = policyName[organizationScopedPrefix.Length..].Split(':', 2);

            if (scopedParts.Length == 2 && !string.IsNullOrWhiteSpace(scopedParts[0]) && !string.IsNullOrWhiteSpace(scopedParts[1]))
            {
                var scopedPolicy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new OrganizationScopedPermissionRequirement(resource: scopedParts[0], action: scopedParts[1]))
                    .Build();

                return Task.FromResult<AuthorizationPolicy?>(scopedPolicy);
            }

            return _fallbackPolicyProvider.GetPolicyAsync(policyName);
        }

        var parts = policyName.Split(':', 2);

        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return _fallbackPolicyProvider.GetPolicyAsync(policyName);
        }

        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(resource: parts[0], action: parts[1]))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

}
