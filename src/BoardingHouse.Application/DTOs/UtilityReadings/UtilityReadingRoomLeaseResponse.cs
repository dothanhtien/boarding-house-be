using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomLeaseResponse
{
    public required Guid LeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required LeaseStatus Status { get; init; }
}
