namespace BoardingHouse.Api.DTOs.Organizations;

public record OrganizationResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? TaxCode { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Province { get; init; }
    public string? District { get; init; }
    public string? Ward { get; init; }
    public string? Address { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public List<OrganizationMemberResponse>? Members { get; init; }
}
