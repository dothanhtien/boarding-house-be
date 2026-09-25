using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.UtilityServices;

public record CreateUtilityServiceRequest
{
    public required Guid PropertyId { get; init; }
    public required string Name { get; init; }
    public required UtilityType Type { get; init; }
    public required string Unit { get; init; }
    public decimal? DefaultUnitPrice { get; init; }
    public bool IsActive { get; init; } = true;
}
