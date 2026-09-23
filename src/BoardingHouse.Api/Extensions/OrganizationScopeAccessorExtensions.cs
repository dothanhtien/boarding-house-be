using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Services;

namespace BoardingHouse.Api.Extensions;

public static class OrganizationScopeAccessorExtensions
{
    public static async Task EnsureScopeAsync(
        this IOrganizationScopeAccessor organizationScopeAccessor,
        Guid organizationId,
        string resource,
        string action,
        string? notFoundMessage = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await organizationScopeAccessor.GetScopeAsync(resource, action, cancellationToken);
        if (!scope.Includes(organizationId))
        {
            throw new AppNotFoundException(notFoundMessage ?? $"Organization '{organizationId}' not found");
        }
    }
}
