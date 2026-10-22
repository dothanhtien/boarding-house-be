using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Property : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Province { get; set; }
    public string? District { get; set; }
    public string? Ward { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public int? DefaultBillingDay { get; set; }
    public LateFeeType? LateFeeType { get; set; }
    public decimal? LateFeeValue { get; set; }
    public int? LateFeeGraceDays { get; set; }
}
