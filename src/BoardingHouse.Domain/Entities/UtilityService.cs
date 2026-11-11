using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class UtilityService : BaseEntity
{
    public Guid PropertyId { get; private set; }
    public Property? Property { get; private set; }

    public string Name { get; private set; } = null!;
    public UtilityType Type { get; private set; }
    public UtilityBillingMethod BillingMethod { get; private set; }
    public string Unit { get; private set; } = null!;
    public decimal DefaultUnitPrice { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool SupportsQuantity => BillingMethod == UtilityBillingMethod.Fixed;

    public UtilityServiceDetails Details => new(Name, BillingMethod, Unit, DefaultUnitPrice, IsActive);

    private UtilityService() { }

    public static UtilityService Create(Guid propertyId, UtilityType type, UtilityServiceDetails details, Guid createdBy)
    {
        EnsureDetailsValid(details);

        var service = new UtilityService { PropertyId = propertyId, Type = type, CreatedBy = createdBy };
        service.Apply(details);
        return service;
    }

    public void Update(UtilityServiceDetails details, UtilityServiceUsage usage)
    {
        EnsureDetailsValid(details);

        if (details.BillingMethod != BillingMethod)
        {
            if (usage.HasFlag(UtilityServiceUsage.Leases))
            {
                throw DomainException.Conflict(
                    "BillingMethod cannot be changed once the service is used by a lease — create a new service instead");
            }

            if (usage.HasFlag(UtilityServiceUsage.Readings))
            {
                throw DomainException.Conflict(
                    "BillingMethod cannot be changed once the service has utility readings — create a new service instead");
            }
        }

        Apply(details);
    }

    private void Apply(UtilityServiceDetails details)
    {
        Name = details.Name;
        BillingMethod = details.BillingMethod;
        Unit = details.Unit;
        DefaultUnitPrice = details.DefaultUnitPrice;
        IsActive = details.IsActive;
    }

    private static void EnsureDetailsValid(UtilityServiceDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
        {
            throw new DomainException("Name is required");
        }

        if (string.IsNullOrWhiteSpace(details.Unit))
        {
            throw new DomainException("Unit is required");
        }

        if (details.DefaultUnitPrice < 0)
        {
            throw new DomainException("DefaultUnitPrice must not be negative");
        }
    }
}
