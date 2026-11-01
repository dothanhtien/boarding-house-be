namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingResponse
{
    public required Guid Id { get; init; }
    public required Guid RoomId { get; init; }
    public required Guid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required string Unit { get; init; }
    public required DateOnly ReadOn { get; init; }
    public required DateOnly ReadingMonth { get; init; }
    public Guid? BilledLeaseId { get; init; }
    public string? BilledLeaseCode { get; init; }
    public decimal? PreviousValue { get; init; }
    public required decimal CurrentValue { get; init; }
    public decimal? Consumption { get; init; }
    public string? ImageUrl { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required Guid CreatedBy { get; init; }
}
