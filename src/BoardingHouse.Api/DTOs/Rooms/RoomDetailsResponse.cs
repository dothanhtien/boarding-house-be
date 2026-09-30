using BoardingHouse.Api.DTOs.RoomAssets;

namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomDetailsResponse : RoomResponse
{
    public required List<RoomAssetResponse> Assets { get; init; }
}
