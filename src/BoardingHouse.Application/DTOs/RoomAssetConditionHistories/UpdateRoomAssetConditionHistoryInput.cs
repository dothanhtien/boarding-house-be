using BoardingHouse.Application.Common;
using BoardingHouse.Application.Services.Storage;

namespace BoardingHouse.Application.DTOs.RoomAssetConditionHistories;

public record UpdateRoomAssetConditionHistoryInput
{
    public Optional<string?> Note { get; init; }
    public IReadOnlyList<Guid> RemoveMediaIds { get; init; } = [];
    public IReadOnlyList<IFileUpload> Files { get; init; } = [];
}
