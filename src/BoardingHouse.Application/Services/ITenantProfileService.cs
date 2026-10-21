using BoardingHouse.Application.DTOs.TenantProfiles;

namespace BoardingHouse.Application.Services;

public interface ITenantProfileService
{
    Task<TenantProfileResponse?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<TenantProfileResponse> UpsertAsync(Guid userId, UpdateTenantProfileRequest request, CancellationToken cancellationToken = default);
}
