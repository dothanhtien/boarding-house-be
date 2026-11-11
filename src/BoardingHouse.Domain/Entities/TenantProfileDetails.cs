using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public sealed record TenantProfileDetails(
    IdentityType? IdentityType,
    string? IdentityNumber,
    DateOnly? IdentityIssuedDate,
    string? IdentityIssuedPlace,
    DateOnly? IdentityExpiryDate,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? PermanentAddress,
    string? EmergencyContactName,
    string? EmergencyContactPhone)
{
    public static readonly TenantProfileDetails Empty = new(null, null, null, null, null, null, null, null, null, null);
}
