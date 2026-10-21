using BoardingHouse.Application.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Services.Storage;

namespace BoardingHouse.Application.Services;

public interface IRoomAssetConditionHistoryService
{
    Task<List<RoomAssetConditionHistoryResponse>> GetAllAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default);
    Task<RoomAssetConditionHistoryResponse> CreateAsync(
        Guid roomId,
        Guid assetId,
        AssetCondition newCondition,
        string? note,
        IReadOnlyList<IFileUpload> files,
        CancellationToken cancellationToken = default);
    Task<RoomAssetConditionHistoryResponse> UpdateAsync(
        Guid roomId,
        Guid assetId,
        Guid historyId,
        Optional<string?> note,
        IReadOnlyList<Guid>? removeMediaIds,
        IReadOnlyList<IFileUpload> files,
        CancellationToken cancellationToken = default);
}
