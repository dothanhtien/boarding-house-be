namespace BoardingHouse.Api.DTOs.Organizations;

public record UpdateOrganizationRequest
{
    public string? Name { get; init; }
    public string? TaxCode { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Province { get; init; }
    public string? District { get; init; }
    public string? Ward { get; init; }
    public string? Address { get; init; }
    public bool? IsActive { get; init; }
}
