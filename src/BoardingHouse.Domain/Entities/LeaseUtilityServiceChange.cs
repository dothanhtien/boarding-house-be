using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public sealed record LeaseUtilityServiceChange
{
    public Change<decimal?> UnitPriceOverride { get; init; }
    public int? Quantity { get; init; }
    public bool? IsActive { get; init; }
}
