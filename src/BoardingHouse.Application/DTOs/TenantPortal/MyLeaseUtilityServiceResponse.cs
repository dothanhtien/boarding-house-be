using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyLeaseUtilityServiceResponse
{
    public required Guid ServiceId { get; init; }
    public required string Name { get; init; }
    public required UtilityType Type { get; init; }
    public required UtilityBillingMethod BillingMethod { get; init; }
    public required string Unit { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int Quantity { get; init; }
}
