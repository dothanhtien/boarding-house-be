using BoardingHouse.Application.DTOs.TenantProfiles;

namespace BoardingHouse.Application.DTOs.Tenants;

public record CreateTenantRequest
{
    public Guid? OrganizationId { get; init; }
    public required string FullName { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required string Password { get; init; }
    public required string PasswordConfirmation { get; init; }
    public UpdateTenantProfileRequest? Profile { get; init; }
}
