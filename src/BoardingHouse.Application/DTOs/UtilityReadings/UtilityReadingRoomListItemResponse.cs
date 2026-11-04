using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomListItemResponse
{
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public required RoomStatus RoomStatus { get; init; }
    public UtilityReadingRoomLeaseResponse? Lease { get; init; }
    public required int MeterCount { get; init; }
    public required int RecordedCount { get; init; }
    public required UtilityReadingRoomStatus Status { get; init; }
}
