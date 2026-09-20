using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities;

public class Organization : BaseEntity
{
    public required string Name { get; set; }
    public string? TaxCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Province { get; set; }
    public string? District { get; set; }
    public string? Ward { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public List<OrganizationMember> Members { get; set; } = [];
}
