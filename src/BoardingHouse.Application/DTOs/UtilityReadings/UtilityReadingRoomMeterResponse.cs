namespace BoardingHouse.Application.DTOs.UtilityReadings;

public record UtilityReadingRoomMeterResponse
{
    public required Guid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required string Unit { get; init; }
    public UtilityReadingSummaryResponse? Latest { get; init; }
    public UtilityReadingResponse? Reading { get; init; }
    public Guid? BilledLeaseId { get; init; }
    public decimal? EffectiveUnitPrice { get; init; }
}
