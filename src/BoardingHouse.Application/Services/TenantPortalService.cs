using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Exceptions;
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

    public Task<PagedResult<MyLeaseResponse>> GetLeasesAsync(MyLeaseListQuery query, CancellationToken cancellationToken = default) =>
        tenantPortalQueries.SearchLeasesAsync(CurrentUserId, query, cancellationToken);

    public async Task<MyLeaseDetailResponse> GetLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        await tenantPortalQueries.GetLeaseAsync(CurrentUserId, leaseId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Lease(leaseId));
}
