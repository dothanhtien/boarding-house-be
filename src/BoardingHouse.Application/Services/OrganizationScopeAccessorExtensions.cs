using BoardingHouse.Application.Common;
using BoardingHouse.Application.Exceptions;

namespace BoardingHouse.Application.Services;

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
        if (!scope.Allows(organizationId))
        {
            throw new AppNotFoundException(notFoundMessage ?? NotFoundMessages.Organization(organizationId));
        }
    }
}
