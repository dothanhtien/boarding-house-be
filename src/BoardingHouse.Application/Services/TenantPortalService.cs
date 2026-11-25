using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Queries;

namespace BoardingHouse.Application.Services;

public class TenantPortalService(
    ITenantPortalQueries tenantPortalQueries,
    ICurrentUserAccessor currentUserAccessor) : ITenantPortalService
{
    private Guid CurrentUserId => currentUserAccessor.RequiredUser.Id;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public Task<TenancyResponse> GetTenancyAsync(CancellationToken cancellationToken = default) =>
        tenantPortalQueries.GetTenancyAsync(CurrentUserId, Today, cancellationToken);
}
