namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record RecordRoomUtilityReadingItemRequest
{
    public required Guid ServiceId { get; init; }
    public required decimal CurrentValue { get; init; }
    public decimal? PreviousValue { get; init; }
}
