namespace BoardingHouse.Application.DTOs.RoomAssetConditionHistories;

public record RoomAssetConditionHistoryMediaResponse
{
    public required Guid MediaId { get; init; }
    public required string Url { get; init; }
    public required string MimeType { get; init; }
    public required string FileName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required Guid CreatedBy { get; init; }
}
