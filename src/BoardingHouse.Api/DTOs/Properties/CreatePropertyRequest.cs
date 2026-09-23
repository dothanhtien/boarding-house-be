using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Properties;

public record CreatePropertyRequest
{
    public Guid? OrganizationId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Province { get; init; }
    public string? District { get; init; }
    public string? Ward { get; init; }
    public string? Address { get; init; }
    public int? DefaultBillingDay { get; init; }
    public LateFeeType? LateFeeType { get; init; }
    public decimal? LateFeeValue { get; init; }
    public int? LateFeeGraceDays { get; init; }
}
