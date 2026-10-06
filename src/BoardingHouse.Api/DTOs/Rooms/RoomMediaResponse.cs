namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomMediaResponse
{
    public required Guid MediaId { get; init; }
    public required string Url { get; init; }
    public required string MimeType { get; init; }
    public required string FileName { get; init; }
    public required int SortOrder { get; init; }
    public required bool IsCover { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
