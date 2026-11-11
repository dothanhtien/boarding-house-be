using Mapster;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Application.Services;

public class RoomAssetConditionHistoryService(
    IRoomAssetRepository roomAssetRepository,
    IRoomAssetConditionHistoryRepository roomAssetConditionHistoryRepository,
    IMediaCollectionService mediaCollectionService,
    IStorageProvider storageProvider,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<RoomAssetConditionHistoryService> logger) : IRoomAssetConditionHistoryService
{
    public async Task<List<RoomAssetConditionHistoryResponse>> GetAllAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var organizationId = await roomAssetRepository.GetOrganizationIdAsync(roomId, assetId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Room,
            PermissionActions.Read,
            NotFoundMessages.RoomAsset(assetId),
            cancellationToken);

        var histories = await roomAssetConditionHistoryRepository.ListByAssetIdAsync(assetId, cancellationToken);
        return histories.Select(ToResponse).ToList();
    }

    public async Task<RoomAssetConditionHistoryResponse> CreateAsync(
        Guid roomId,
        Guid assetId,
        CreateRoomAssetConditionHistoryInput input,
        CancellationToken cancellationToken = default)
    {
        var files = input.Files;
        RoomAssetConditionHistory history;
        if (files.Count == 0)
        {
            history = await unitOfWork.ExecuteInTransactionAsync(
                ct => AppendEntryAsync(roomId, assetId, input, [], ct),
                cancellationToken);

            logger.LogInformation(
                "Room asset condition recorded ({RoomAssetId}), history {RoomAssetConditionHistoryId}", assetId, history.Id);
        }
        else
        {
            var organizationId = await roomAssetRepository.GetOrganizationIdAsync(roomId, assetId, cancellationToken)
                ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId,
                PermissionResources.Room,
                PermissionActions.Update,
                NotFoundMessages.RoomAsset(assetId),
                cancellationToken);

            history = await mediaCollectionService.AddAsync(
                MediaAssetEntityType.RoomAssetConditionPhoto,
                files,
                PhotoFolder(organizationId, roomId, assetId),
                organizationId,
                (uploaded, ct) => AppendEntryAsync(roomId, assetId, input, uploaded, ct),
                cancellationToken);

            logger.LogInformation(
                "Room asset condition recorded with photos ({RoomAssetId}), history {RoomAssetConditionHistoryId}, {Count} photos",
                assetId, history.Id, files.Count);
        }

        return ToResponse(history);
    }

    public async Task<RoomAssetConditionHistoryResponse> UpdateAsync(
        Guid roomId,
        Guid assetId,
        Guid historyId,
        UpdateRoomAssetConditionHistoryInput input,
        CancellationToken cancellationToken = default)
    {
        var (note, removeIds, files) = (input.Note, input.RemoveMediaIds, input.Files);
        var historyNotFoundMessage = NotFoundMessages.RoomAssetConditionHistory(historyId);

        (RoomAssetConditionHistory History, IReadOnlyList<MediaAsset> Removed) result;
        if (files.Count == 0)
        {
            result = await unitOfWork.ExecuteInTransactionAsync(
                ct => ApplyUpdateAsync(roomId, assetId, historyId, note, removeIds, [], ct),
                cancellationToken);
        }
        else
        {
            var organizationId = await roomAssetRepository.GetOrganizationIdAsync(roomId, assetId, cancellationToken)
                ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId,
                PermissionResources.Room,
                PermissionActions.Update,
                NotFoundMessages.RoomAsset(assetId),
                cancellationToken);

            var current = await roomAssetConditionHistoryRepository.GetByIdInAssetWithMediaAsync(
                    assetId, historyId, asNoTracking: true, cancellationToken)
                ?? throw new AppNotFoundException(historyNotFoundMessage);

            EnsureLatest(current);
            current.History.EnsureCanUpdatePhotos(removeIds, files.Count);

            result = await mediaCollectionService.AddAsync(
                MediaAssetEntityType.RoomAssetConditionPhoto,
                files,
                PhotoFolder(organizationId, roomId, assetId),
                organizationId,
                (uploaded, ct) => ApplyUpdateAsync(roomId, assetId, historyId, note, removeIds, uploaded, ct),
                cancellationToken);
        }

        await mediaCollectionService.CompleteAsync(result.Removed);

        logger.LogInformation(
            "Room asset condition history updated ({RoomAssetConditionHistoryId}), {Removed} photos removed, {Added} added",
            historyId, result.Removed.Count, files.Count);

        return ToResponse(result.History);
    }

    private async Task<RoomAssetConditionHistory> AppendEntryAsync(
        Guid roomId,
        Guid assetId,
        CreateRoomAssetConditionHistoryInput input,
        IReadOnlyList<MediaAsset> uploaded,
        CancellationToken cancellationToken)
    {
        var (asset, organizationId) = await roomAssetRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, assetId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Room,
            PermissionActions.Update,
            NotFoundMessages.RoomAsset(assetId),
            cancellationToken);

        var created = asset.ChangeCondition(
            input.NewCondition, input.Note, uploaded, currentUserAccessor.RequiredUser.Id, DateTimeOffset.UtcNow);

        await roomAssetConditionHistoryRepository.AddAsync(created, cancellationToken);

        return created;
    }

    private async Task<(RoomAssetConditionHistory History, IReadOnlyList<MediaAsset> Removed)> ApplyUpdateAsync(
        Guid roomId,
        Guid assetId,
        Guid historyId,
        Optional<string?> note,
        IReadOnlyCollection<Guid> removeIds,
        IReadOnlyList<MediaAsset> uploaded,
        CancellationToken cancellationToken)
    {
        var (_, organizationId) = await roomAssetRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, assetId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Room,
            PermissionActions.Update,
            NotFoundMessages.RoomAsset(assetId),
            cancellationToken);

        var entry = await roomAssetConditionHistoryRepository.GetByIdInAssetWithMediaAsync(
                assetId, historyId, asNoTracking: false, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.RoomAssetConditionHistory(historyId));

        EnsureLatest(entry);
        var history = entry.History;

        var removedAssets = history.Update(
            note.ToChange(), removeIds, uploaded, currentUserAccessor.RequiredUser.Id, DateTimeOffset.UtcNow);
        mediaCollectionService.StageRemove(removedAssets);

        return (history, removedAssets);
    }

    private static void EnsureLatest(RoomAssetConditionHistoryEntry entry)
    {
        if (!entry.IsLatest)
        {
            throw new AppConflictException(
                "Only the latest condition entry of an asset can be updated; record a new condition entry instead");
        }
    }

    private RoomAssetConditionHistoryResponse ToResponse(RoomAssetConditionHistory history) =>
        history.Adapt<RoomAssetConditionHistoryResponse>() with
        {
            Media = history.Media.ToMediaResponses(storageProvider)
        };

    internal static string PhotoFolder(Guid organizationId, Guid roomId, Guid assetId) =>
        $"organizations/{organizationId}/rooms/{roomId}/assets/{assetId}";
}
