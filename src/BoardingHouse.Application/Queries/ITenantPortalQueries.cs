using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;

namespace BoardingHouse.Application.Queries;

public interface ITenantPortalQueries
{
    Task<TenancyResponse> GetTenancyAsync(Guid userId, DateOnly today, CancellationToken cancellationToken = default);

    Task<PagedResult<MyLeaseResponse>> SearchLeasesAsync(
        Guid userId, MyLeaseListQuery query, CancellationToken cancellationToken = default);
    Task<MyLeaseDetailResponse?> GetLeaseAsync(Guid userId, Guid leaseId, CancellationToken cancellationToken = default);
}
