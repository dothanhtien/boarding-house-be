using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class LeaseUtilityService : IAuditable
{
    public Guid LeaseId { get; set; }
    public Lease? Lease { get; set; }

    public Guid ServiceId { get; set; }
    public UtilityService? Service { get; set; }

    public decimal? UnitPriceOverride { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public required Guid CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public decimal? EffectiveUnitPrice => UnitPriceOverride ?? RequiredService.DefaultUnitPrice;

    private UtilityService RequiredService =>
        Service ?? throw new InvalidOperationException("LeaseUtilityService.Service must be loaded to resolve its unit price");
}
