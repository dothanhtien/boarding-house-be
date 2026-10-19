namespace BoardingHouse.Application.DTOs.Tenants;

public record TenantLookupQuery
{
    public string? Phone { get; init; }
    public string? Email { get; init; }
}
