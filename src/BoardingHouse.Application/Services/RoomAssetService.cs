using Mapster;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.DTOs.RoomAssets;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Application.Services;

public class RoomAssetService(
    IRoomRepository roomRepository,
    IRoomAssetRepository roomAssetRepository,
    IRoomAssetConditionHistoryRepository roomAssetConditionHistoryRepository,
    IMediaCollectionService mediaCollectionService,
    IStorageProvider storageProvider,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<RoomAssetService> logger) : IRoomAssetService
{
    public async Task<RoomDetailsAssetResponse> CreateAsync(
        Guid roomId,
        CreateRoomAssetInput input,
        CancellationToken cancellationToken = default)
    {
        var assetId = Guid.NewGuid();
        var files = input.Files;

        (RoomAsset Asset, RoomAssetConditionHistory History) created;
        if (files.Count == 0)
        {
            created = await unitOfWork.ExecuteInTransactionAsync(
                ct => AddAssetAsync(roomId, assetId, input, [], ct), cancellationToken);
        }
        else
        {
            var room = await roomRepository.GetByIdWithPropertyAsync(roomId, cancellationToken)
                ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

            var organizationId = room.Property!.OrganizationId;
            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.Room(roomId), cancellationToken);

            created = await mediaCollectionService.AddAsync(
                MediaAssetEntityType.RoomAssetConditionPhoto,
                files,
                RoomAssetConditionHistoryService.PhotoFolder(organizationId, roomId, assetId),
                organizationId,
                (uploaded, ct) => AddAssetAsync(roomId, assetId, input, uploaded, ct),
                cancellationToken);
        }

        logger.LogInformation("Room asset created ({RoomAssetId}), {Count} photos", assetId, files.Count);

        var (asset, history) = created;
        var response = asset.Adapt<RoomDetailsAssetResponse>();
        return history.Media.Count == 0
            ? response
            : response with
            {
                CurrentPhotos = history.ToCurrentPhotosResponse(isLatest: true, storageProvider)
            };
    }

    private async Task<(RoomAsset Asset, RoomAssetConditionHistory History)> AddAssetAsync(
        Guid roomId,
        Guid assetId,
        CreateRoomAssetInput input,
        IReadOnlyList<MediaAsset> uploaded,
        CancellationToken cancellationToken)
    {
        var (_, organizationId) = await roomRepository.GetByIdWithOrganizationIdForShareAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.Room(roomId), cancellationToken);

        var (asset, history) = RoomAsset.Create(
            assetId,
            roomId,
            input.Name,
            input.Quantity,
            input.PurchaseDate,
            input.PurchaseUnitPrice,
            input.Note,
            uploaded,
            currentUserAccessor.RequiredUser.Id,
            DateTimeOffset.UtcNow);

        await roomAssetRepository.AddAsync(asset, cancellationToken);
        await roomAssetConditionHistoryRepository.AddAsync(history, cancellationToken);

        return (asset, history);
    }

    public async Task<RoomAssetResponse> UpdateAsync(Guid roomId, Guid assetId, UpdateRoomAssetRequest request, CancellationToken cancellationToken = default)
    {
        var asset = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (existing, organizationId) = await roomAssetRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.RoomAsset(assetId), ct);

            existing.Update(
                request.Name.OrKeep(existing.Name)!,
                request.Quantity.OrKeep(existing.Quantity),
                request.PurchaseDate.OrKeep(existing.PurchaseDate),
                request.PurchaseUnitPrice.OrKeep(existing.PurchaseUnitPrice),
                request.Note.OrKeep(existing.Note));

            return existing;
        }, cancellationToken);

        logger.LogInformation("Room asset updated ({RoomAssetId})", asset.Id);

        return asset.Adapt<RoomAssetResponse>();
    }

    public async Task DeleteAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var removedPhotos = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (existing, organizationId) = await roomAssetRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.RoomAsset(assetId), ct);

            var histories = await roomAssetConditionHistoryRepository.ListWithPhotosByAssetIdsAsync([existing.Id], ct);
            var photos = histories.SelectMany(h => h.RemoveAllPhotos()).ToList();
            mediaCollectionService.StageRemove(photos);
            roomAssetRepository.SoftDelete(existing);

            return photos;
        }, cancellationToken);

        await mediaCollectionService.CompleteAsync(removedPhotos);

        logger.LogInformation("Room asset soft-deleted ({RoomAssetId}), {Count} condition photos removed", assetId, removedPhotos.Count);
    }

    public async Task<SplitRoomAssetResponse> SplitAsync(Guid roomId, Guid assetId, SplitRoomAssetRequest request, CancellationToken cancellationToken = default)
    {
        var (original, split) = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            _ = await roomRepository.GetByIdWithOrganizationIdForShareAsync(roomId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

            var (existing, organizationId) = await roomAssetRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.RoomAsset(assetId));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.RoomAsset(assetId), ct);

            var (created, history) = existing.Split(
                request.Quantity, request.NewCondition, request.Note, currentUserAccessor.RequiredUser.Id, DateTimeOffset.UtcNow);

            await roomAssetRepository.AddAsync(created, ct);

            await roomAssetConditionHistoryRepository.AddAsync(history, ct);

            return (existing, created);
        }, cancellationToken);

        logger.LogInformation(
            "Room asset split ({RoomAssetId} -> {SplitRoomAssetId}, quantity {Quantity})", original.Id, split.Id, request.Quantity);

        return new SplitRoomAssetResponse
        {
            Original = original.Adapt<RoomAssetResponse>(),
            Split = split.Adapt<RoomAssetResponse>()
        };
    }
}
