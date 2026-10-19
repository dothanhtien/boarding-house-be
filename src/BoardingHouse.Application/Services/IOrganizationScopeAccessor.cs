using BoardingHouse.Application.Common;

namespace BoardingHouse.Application.Services;

public interface IOrganizationScopeAccessor
{
    Task<OrganizationScope> GetScopeAsync(string resource, string action, CancellationToken cancellationToken = default);
}
