using BoardingHouse.Application.DTOs.RoomAssets;

namespace BoardingHouse.Application.DTOs.Rooms;

public record RoomDetailsAssetResponse : RoomAssetResponse
{
    public RoomAssetCurrentPhotosResponse? CurrentPhotos { get; init; }
}
