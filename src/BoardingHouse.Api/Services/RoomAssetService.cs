using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Storage;
using Mapster;

namespace BoardingHouse.Api.Services;

public class RoomAssetService(IRoomRepository roomRepository,
    IRoomAssetRepository roomAssetRepository,
    IRoomAssetConditionHistoryRepository roomAssetConditionHistoryRepository,
    IMediaCollectionService mediaCollectionService,
    IStorageProvider storageProvider,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<RoomAssetService> logger) : IRoomAssetService
{
    public async Task<RoomDetailsAssetResponse> CreateAsync(Guid roomId, CreateRoomAssetRequest request, CancellationToken cancellationToken = default)
    {
        var files = request.Files ?? [];
        var assetId = Guid.NewGuid();

        (RoomAsset Asset, RoomAssetConditionHistory History) created;
        if (files.Count == 0)
        {
            created = await unitOfWork.ExecuteInTransactionAsync(ct => AddAssetAsync(roomId, assetId, request, [], ct), cancellationToken);
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
                (uploaded, ct) => AddAssetAsync(roomId, assetId, request, uploaded, ct),
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
        CreateRoomAssetRequest request,
        IReadOnlyList<MediaAsset> uploaded,
        CancellationToken cancellationToken)
    {
        var (_, organizationId) = await roomRepository.GetByIdWithOrganizationIdForShareAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.Room(roomId), cancellationToken);

        var actorId = currentUserAccessor.RequiredUser.Id;
        var asset = new RoomAsset
        {
            Id = assetId,
            RoomId = roomId,
            Name = request.Name,
            Quantity = request.Quantity,
            PurchaseDate = request.PurchaseDate,
            PurchaseUnitPrice = request.PurchaseUnitPrice,
            Condition = AssetCondition.Good,
            Note = request.Note,
            CreatedBy = actorId
        };
        await roomAssetRepository.AddAsync(asset, cancellationToken);

        var history = new RoomAssetConditionHistory
        {
            AssetId = assetId,
            OldCondition = null,
            NewCondition = asset.Condition,
            ChangedBy = actorId
        };

        foreach (var (media, i) in uploaded.Select((media, i) => (media, i)))
        {
            history.Media.Add(new RoomAssetConditionHistoryMedia
            {
                ConditionHistoryId = history.Id,
                MediaAssetId = media.Id,
                MediaAsset = media,
                SortOrder = i,
                CreatedBy = actorId
            });
        }

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

            request.Adapt(existing, RoomAssetMappingConfig.UpdateConfig);

            roomAssetRepository.Update(existing);

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

            var photos = await roomAssetConditionHistoryRepository.RemoveMediaByAssetIdsAsync([existing.Id], ct);
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

            if (request.Quantity >= existing.Quantity)
            {
                throw new AppValidationException(
                    $"Quantity must be less than the asset's quantity ({existing.Quantity}); to change the condition of the whole row, record a condition history entry instead");
            }

            var actorId = currentUserAccessor.RequiredUser.Id;
            var created = new RoomAsset
            {
                RoomId = roomId,
                Name = existing.Name,
                Quantity = request.Quantity,
                PurchaseDate = existing.PurchaseDate,
                PurchaseUnitPrice = existing.PurchaseUnitPrice,
                Condition = request.NewCondition,
                Note = existing.Note,
                SplitFromAssetId = existing.Id,
                CreatedBy = actorId
            };

            existing.Quantity -= request.Quantity;
            roomAssetRepository.Update(existing);
            await roomAssetRepository.AddAsync(created, ct);

            await roomAssetConditionHistoryRepository.AddAsync(new RoomAssetConditionHistory
            {
                AssetId = created.Id,
                OldCondition = existing.Condition,
                NewCondition = request.NewCondition,
                Note = request.Note,
                ChangedBy = actorId
            }, ct);

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
