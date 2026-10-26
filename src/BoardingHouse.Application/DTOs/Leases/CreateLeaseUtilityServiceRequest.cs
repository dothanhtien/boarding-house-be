namespace BoardingHouse.Application.DTOs.Leases;

public record CreateLeaseUtilityServiceRequest
{
    public required Guid ServiceId { get; init; }
    public decimal? UnitPriceOverride { get; init; }
    public int Quantity { get; init; } = 1;
}
