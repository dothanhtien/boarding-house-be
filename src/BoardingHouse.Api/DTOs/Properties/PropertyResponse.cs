using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Properties;

public record PropertyResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Province { get; init; }
    public string? District { get; init; }
    public string? Ward { get; init; }
    public string? Address { get; init; }
    public required bool IsActive { get; init; }
    public int? DefaultBillingDay { get; init; }
    public LateFeeType? LateFeeType { get; init; }
    public decimal? LateFeeValue { get; init; }
    public int? LateFeeGraceDays { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
