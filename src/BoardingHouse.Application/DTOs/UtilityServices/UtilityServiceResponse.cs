using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.UtilityServices;

public record UtilityServiceResponse
{
    public required Guid Id { get; init; }
    public required Guid PropertyId { get; init; }
    public required string Name { get; init; }
    public required UtilityType Type { get; init; }
    public required UtilityBillingMethod BillingMethod { get; init; }
    public required string Unit { get; init; }
    public decimal? DefaultUnitPrice { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
