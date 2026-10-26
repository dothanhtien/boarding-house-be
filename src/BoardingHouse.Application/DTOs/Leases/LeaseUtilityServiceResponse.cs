using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Leases;

public record LeaseUtilityServiceResponse
{
    public required Guid ServiceId { get; init; }
    public required string Name { get; init; }
    public required UtilityType Type { get; init; }
    public required UtilityBillingMethod BillingMethod { get; init; }
    public required string Unit { get; init; }
    public required decimal DefaultUnitPrice { get; init; }
    public decimal? UnitPriceOverride { get; init; }
    public required decimal EffectiveUnitPrice { get; init; }
    public required int Quantity { get; init; }
    public required bool IsActive { get; init; }
    public required bool ServiceIsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
