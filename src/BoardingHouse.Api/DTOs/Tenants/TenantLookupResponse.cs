namespace BoardingHouse.Api.DTOs.Tenants;

public record TenantLookupResponse
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public string? MaskedPhone { get; init; }
    public string? MaskedEmail { get; init; }
    public required bool HasTenantProfile { get; init; }
    public required bool IsActive { get; init; }
}
