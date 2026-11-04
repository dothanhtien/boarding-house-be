using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Common;

public sealed record RoomWithRecordedMeterCount
{
    public required Room Room { get; init; }
    public required int MeterCount { get; init; }
    public required int RecordedCount { get; init; }
}
