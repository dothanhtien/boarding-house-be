using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class UtilityService : BaseEntity
{
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }

    public required string Name { get; set; }
    public UtilityType Type { get; set; }
    public required string Unit { get; set; }
    public decimal? DefaultUnitPrice { get; set; }
    public bool IsActive { get; set; } = true;
}
