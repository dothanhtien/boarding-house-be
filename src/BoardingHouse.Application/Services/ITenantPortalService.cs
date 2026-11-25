using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;

namespace BoardingHouse.Application.Services;

public interface ITenantPortalService
{
    Task<TenancyResponse> GetTenancyAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<MyLeaseResponse>> GetLeasesAsync(MyLeaseListQuery query, CancellationToken cancellationToken = default);
    Task<MyLeaseDetailResponse> GetLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default);
}
