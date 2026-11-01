using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomChargeResponse
{
    public required Guid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required UtilityBillingMethod BillingMethod { get; init; }
    public required string Unit { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int Quantity { get; init; }
    public required decimal Amount { get; init; }
}
