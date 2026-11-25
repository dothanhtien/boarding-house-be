using BoardingHouse.Application.DTOs.TenantPortal;

namespace BoardingHouse.Application.Queries;

public interface ITenantPortalQueries
{
    Task<TenancyResponse> GetTenancyAsync(Guid userId, DateOnly today, CancellationToken cancellationToken = default);
}
