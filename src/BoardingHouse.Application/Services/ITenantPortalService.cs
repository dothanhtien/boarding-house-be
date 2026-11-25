using BoardingHouse.Application.DTOs.TenantPortal;

namespace BoardingHouse.Application.Services;

public interface ITenantPortalService
{
    Task<TenancyResponse> GetTenancyAsync(CancellationToken cancellationToken = default);
}
