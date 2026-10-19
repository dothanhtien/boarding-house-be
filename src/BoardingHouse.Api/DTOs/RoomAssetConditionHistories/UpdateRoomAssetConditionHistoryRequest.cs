using BoardingHouse.Application.Common;

namespace BoardingHouse.Api.DTOs.RoomAssetConditionHistories;

public record UpdateRoomAssetConditionHistoryRequest
{
    public Optional<string?> Note { get; init; }
    public List<Guid>? RemoveMediaIds { get; init; }
    public List<IFormFile>? Files { get; init; }
}
