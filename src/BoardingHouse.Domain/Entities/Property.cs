using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Property : BaseEntity
{
    public Guid OrganizationId { get; private set; }
    public Organization? Organization { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Province { get; private set; }
    public string? District { get; private set; }
    public string? Ward { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int? DefaultBillingDay { get; private set; }
    public LateFeeType? LateFeeType { get; private set; }
    public decimal? LateFeeValue { get; private set; }
    public int? LateFeeGraceDays { get; private set; }

    public LateFeePolicy LateFee => new(LateFeeType, LateFeeValue, LateFeeGraceDays);

    public PropertyDetails Details =>
        new(Name, Description, Province, District, Ward, Address, IsActive, DefaultBillingDay, LateFee);

    private Property() { }

    public static Property Create(Guid organizationId, PropertyDetails details, Guid createdBy)
    {
        var property = new Property { OrganizationId = organizationId, CreatedBy = createdBy };
        property.Update(details);
        return property;
    }

    public void Update(PropertyDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
        {
            throw new DomainException("Name is required");
        }

        BillingDayRule.EnsureValid(details.DefaultBillingDay, nameof(DefaultBillingDay));

        Name = details.Name;
        Description = details.Description;
        Province = details.Province;
        District = details.District;
        Ward = details.Ward;
        Address = details.Address;
        IsActive = details.IsActive;
        DefaultBillingDay = details.DefaultBillingDay;
        LateFeeType = details.LateFee.Type;
        LateFeeValue = details.LateFee.Value;
        LateFeeGraceDays = details.LateFee.GraceDays;
    }
}
