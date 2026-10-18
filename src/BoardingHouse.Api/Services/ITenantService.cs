using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.DTOs.Tenants;

namespace BoardingHouse.Api.Services;

public interface ITenantService
{
    Task<TenantLookupResponse> LookupAsync(TenantLookupQuery query, CancellationToken cancellationToken = default);
    Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<TenantResponse> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<TenantProfileResponse> UpdateProfileAsync(Guid userId, UpdateTenantProfileRequest request, CancellationToken cancellationToken = default);
}
