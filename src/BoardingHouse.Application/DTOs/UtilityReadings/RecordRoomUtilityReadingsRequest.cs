namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record RecordRoomUtilityReadingsRequest
{
    public const int MaxItems = 20;

    public DateOnly? ReadOn { get; init; }
    public DateOnly? ReadingMonth { get; init; }
    public required List<RecordRoomUtilityReadingItemRequest> Items { get; init; }
}
