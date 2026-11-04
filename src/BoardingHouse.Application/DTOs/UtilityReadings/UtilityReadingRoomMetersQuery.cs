namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomMetersQuery
{
    public DateOnly? ReadingMonth { get; init; }
}
