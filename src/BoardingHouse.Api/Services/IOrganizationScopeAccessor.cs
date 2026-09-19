using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Services;

public interface IOrganizationScopeAccessor
{
    Task<OrganizationScope> GetScopeAsync(string resource, string action, CancellationToken cancellationToken = default);
}
