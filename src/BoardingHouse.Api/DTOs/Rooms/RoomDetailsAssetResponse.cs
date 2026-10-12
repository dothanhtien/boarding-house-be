using BoardingHouse.Api.DTOs.RoomAssets;

namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomDetailsAssetResponse : RoomAssetResponse
{
    public RoomAssetCurrentPhotosResponse? CurrentPhotos { get; init; }
}
