namespace BoardingHouse.Application.DTOs.Rooms;

public record RoomDetailsResponse : RoomResponse
{
    public required List<RoomDetailsAssetResponse> Assets { get; init; }
    public required List<RoomMediaResponse> Media { get; init; }
}
