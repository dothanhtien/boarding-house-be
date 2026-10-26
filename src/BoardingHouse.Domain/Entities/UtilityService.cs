using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class UtilityService : BaseEntity
{
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }

    public required string Name { get; set; }
    public UtilityType Type { get; set; }
    public UtilityBillingMethod BillingMethod { get; set; }
    public required string Unit { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public bool IsActive { get; set; } = true;

    public bool SupportsQuantity => BillingMethod == UtilityBillingMethod.Fixed;
}
