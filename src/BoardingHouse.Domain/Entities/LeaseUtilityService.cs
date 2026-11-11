using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class LeaseUtilityService : IAuditable
{
    public Guid LeaseId { get; private set; }
    public Lease? Lease { get; private set; }

    public Guid ServiceId { get; private set; }
    public UtilityService? Service { get; private set; }

    public decimal? UnitPriceOverride { get; private set; }
    public int Quantity { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public decimal EffectiveUnitPrice => UnitPriceOverride ?? RequiredService.DefaultUnitPrice;

    public bool IsBillable => IsActive && RequiredService is { IsActive: true, IsDeleted: false };

    private LeaseUtilityService() { }

    public LeaseUtilityService(
        Guid leaseId, UtilityService service, decimal? unitPriceOverride, int quantity, Guid createdBy)
    {
        LeaseId = leaseId;
        ServiceId = service.Id;
        Service = service;
        CreatedBy = createdBy;
        SetUnitPriceOverride(unitPriceOverride);
        SetQuantity(quantity);
    }

    public int BillableQuantity(int tenantCount) => RequiredService.BillingMethod switch
    {
        UtilityBillingMethod.PerTenant => tenantCount,
        UtilityBillingMethod.Fixed => Quantity,
        _ => throw new InvalidOperationException(
            $"Utility service '{RequiredService.Name}' is metered: its charge comes from the meter reading")
    };

    public decimal ChargeFor(int tenantCount) => EffectiveUnitPrice * BillableQuantity(tenantCount);

    public void SetUnitPriceOverride(decimal? unitPriceOverride)
    {
        if (unitPriceOverride < 0)
        {
            throw new DomainException("UnitPriceOverride must not be negative");
        }

        UnitPriceOverride = unitPriceOverride;
    }

    public void SetQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than 0");
        }

        if (quantity != 1 && !RequiredService.SupportsQuantity)
        {
            throw new DomainException(
                $"Utility service '{RequiredService.Name}' is billed {RequiredService.BillingMethod} and cannot have a quantity");
        }

        Quantity = quantity;
    }

    public void SetIsActive(bool isActive) => IsActive = isActive;

    private UtilityService RequiredService =>
        Service ?? throw new InvalidOperationException("LeaseUtilityService.Service must be loaded");
}
