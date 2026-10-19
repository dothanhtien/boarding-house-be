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
using BoardingHouse.Application.Services;
using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Services;

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
        CreateRoomAssetConditionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var files = request.Files.ToFileUploads();
        var newCondition = request.NewCondition!.Value;

        RoomAssetConditionHistory history;
        if (files.Count == 0)
        {
            history = await unitOfWork.ExecuteInTransactionAsync(
                ct => AppendEntryAsync(roomId, assetId, newCondition, request.Note, [], ct),
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
                (uploaded, ct) => AppendEntryAsync(roomId, assetId, newCondition, request.Note, uploaded, ct),
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
        UpdateRoomAssetConditionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var files = request.Files.ToFileUploads();
        var removeIds = request.RemoveMediaIds ?? [];
        var historyNotFoundMessage = NotFoundMessages.RoomAssetConditionHistory(historyId);

        (RoomAssetConditionHistory History, List<MediaAsset> Removed) result;
        if (files.Count == 0)
        {
            result = await unitOfWork.ExecuteInTransactionAsync(
                ct => ApplyUpdateAsync(roomId, assetId, historyId, request.Note, removeIds, [], ct),
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
            EnsureCanUpdate(current, removeIds, files.Count);

            result = await mediaCollectionService.AddAsync(
                MediaAssetEntityType.RoomAssetConditionPhoto,
                files,
                PhotoFolder(organizationId, roomId, assetId),
                organizationId,
                (uploaded, ct) => ApplyUpdateAsync(roomId, assetId, historyId, request.Note, removeIds, uploaded, ct),
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
        AssetCondition newCondition,
        string? note,
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

        var userId = currentUserAccessor.RequiredUser.Id;
        var created = new RoomAssetConditionHistory
        {
            AssetId = assetId,
            OldCondition = asset.Condition,
            NewCondition = newCondition,
            Note = note,
            ChangedBy = userId
        };

        for (var i = 0; i < uploaded.Count; i++)
        {
            created.Media.Add(new RoomAssetConditionHistoryMedia
            {
                ConditionHistoryId = created.Id,
                MediaAssetId = uploaded[i].Id,
                MediaAsset = uploaded[i],
                SortOrder = i,
                CreatedBy = userId
            });
        }

        asset.Condition = newCondition;
        roomAssetRepository.Update(asset);
        await roomAssetConditionHistoryRepository.AddAsync(created, cancellationToken);

        return created;
    }

    private async Task<(RoomAssetConditionHistory History, List<MediaAsset> Removed)> ApplyUpdateAsync(
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
        EnsureCanUpdate(entry, removeIds, uploaded.Count);
        var history = entry.History;

        note.ApplyIfSet(value => history.Note = value);

        var removed = history.Media.Where(m => removeIds.Contains(m.MediaAssetId)).ToList();
        foreach (var media in removed)
        {
            roomAssetConditionHistoryRepository.RemoveMedia(history, media);
        }

        var removedAssets = removed.Select(m => m.MediaAsset!).ToList();
        mediaCollectionService.StageRemove(removedAssets);

        var userId = currentUserAccessor.RequiredUser.Id;
        var nextSortOrder = MediaCollectionRules.NextSortOrder(history.Media.Select(m => m.SortOrder));
        for (var i = 0; i < uploaded.Count; i++)
        {
            roomAssetConditionHistoryRepository.AddMedia(history, new RoomAssetConditionHistoryMedia
            {
                ConditionHistoryId = history.Id,
                MediaAssetId = uploaded[i].Id,
                MediaAsset = uploaded[i],
                SortOrder = nextSortOrder + i,
                CreatedBy = userId
            });
        }

        if (removed.Count > 0 || uploaded.Count > 0)
        {
            roomAssetConditionHistoryRepository.MarkUpdated(history);
        }

        return (history, removedAssets);
    }

    private static void EnsureCanUpdate(
        RoomAssetConditionHistoryEntry entry,
        IReadOnlyCollection<Guid> removeIds,
        int addedCount)
    {
        if (!entry.IsLatest)
        {
            throw new AppConflictException(
                "Only the latest condition entry of an asset can be updated; record a new condition entry instead");
        }

        var currentMediaIds = entry.History.Media.Select(m => m.MediaAssetId).ToHashSet();
        var unknown = removeIds.Where(id => !currentMediaIds.Contains(id)).Cast<Guid?>().FirstOrDefault();
        if (unknown is not null)
        {
            throw new AppNotFoundException(NotFoundMessages.RoomAssetConditionPhoto(unknown.Value));
        }

        if (addedCount == 0)
        {
            return;
        }

        MediaCollectionRules.EnsureWithinLimit(
            currentMediaIds.Count - removeIds.Count, addedCount, RoomAssetConditionHistory.MaxMedia,
            $"A condition entry must not have more than {RoomAssetConditionHistory.MaxMedia} photos");
    }

    private RoomAssetConditionHistoryResponse ToResponse(RoomAssetConditionHistory history) =>
        history.Adapt<RoomAssetConditionHistoryResponse>() with
        {
            Media = history.Media.ToMediaResponses(storageProvider)
        };

    internal static string PhotoFolder(Guid organizationId, Guid roomId, Guid assetId) =>
        $"organizations/{organizationId}/rooms/{roomId}/assets/{assetId}";
}
