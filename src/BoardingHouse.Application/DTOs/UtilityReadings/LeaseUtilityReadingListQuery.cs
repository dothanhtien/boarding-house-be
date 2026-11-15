using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record LeaseUtilityReadingListQuery : PageRequest
{
    public Guid? ServiceId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public bool? Invoiced { get; init; }
    public string? SortBy { get; init; }
    public SortOrder SortOrder { get; init; } = SortOrder.Desc;
}
