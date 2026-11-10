namespace BoardingHouse.Domain.Entities;

public sealed record OrganizationDetails(
    string Name,
    string? TaxCode,
    string? Phone,
    string? Email,
    string? Province,
    string? District,
    string? Ward,
    string? Address,
    bool IsActive);
