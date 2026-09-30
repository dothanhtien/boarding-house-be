namespace BoardingHouse.Api.DTOs.RoomAssets;

public record SplitRoomAssetResponse
{
    public required RoomAssetResponse Original { get; init; }
    public required RoomAssetResponse Split { get; init; }
}
