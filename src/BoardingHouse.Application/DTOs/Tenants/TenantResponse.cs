using BoardingHouse.Application.DTOs.TenantProfiles;

namespace BoardingHouse.Application.DTOs.Tenants;

public record TenantResponse
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public TenantProfileResponse? Profile { get; init; }

    public IReadOnlyList<TenantLeaseResponse> Leases { get; init; } = [];
}
