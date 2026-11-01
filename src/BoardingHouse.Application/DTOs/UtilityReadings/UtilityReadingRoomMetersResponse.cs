using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomMetersResponse
{
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public required RoomStatus RoomStatus { get; init; }
    public required DateOnly ReadingMonth { get; init; }
    public UtilityReadingRoomLeaseResponse? Lease { get; init; }
    public required List<UtilityReadingRoomMeterResponse> Meters { get; init; }
    public required List<UtilityReadingRoomChargeResponse> OtherCharges { get; init; }
}
