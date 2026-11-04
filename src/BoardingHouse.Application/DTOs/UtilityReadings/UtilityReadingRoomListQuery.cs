using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomListQuery : PageRequest
{
    public DateOnly? ReadingMonth { get; init; }
    public bool? Recorded { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public SortOrder SortOrder { get; init; } = SortOrder.Asc;
}
