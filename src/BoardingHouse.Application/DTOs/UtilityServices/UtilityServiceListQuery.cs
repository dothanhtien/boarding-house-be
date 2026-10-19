using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.UtilityServices;

public record UtilityServiceListQuery
{
    public Guid PropertyId { get; init; }
    public UtilityType? Type { get; init; }
    public bool? IsActive { get; init; }
}
