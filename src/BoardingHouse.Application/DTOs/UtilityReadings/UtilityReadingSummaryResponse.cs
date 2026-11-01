namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingSummaryResponse
{
    public required Guid Id { get; init; }
    public required DateOnly ReadOn { get; init; }
    public required DateOnly ReadingMonth { get; init; }
    public required decimal CurrentValue { get; init; }
}
