using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.TenantProfiles;

public record TenantProfileResponse
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public IdentityType? IdentityType { get; init; }
    public string? IdentityNumber { get; init; }
    public DateOnly? IdentityIssuedDate { get; init; }
    public string? IdentityIssuedPlace { get; init; }
    public DateOnly? IdentityExpiryDate { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    public string? PermanentAddress { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
