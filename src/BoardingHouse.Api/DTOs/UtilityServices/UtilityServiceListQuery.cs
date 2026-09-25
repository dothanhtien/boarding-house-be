using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.UtilityServices;

public record UtilityServiceListQuery
{
    public Guid PropertyId { get; init; }
    public UtilityType? Type { get; init; }
    public bool? IsActive { get; init; }
}
