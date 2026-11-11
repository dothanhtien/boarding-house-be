using Mapster;
using System.Linq.Expressions;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public class RoomService(
    IRoomRepository roomRepository,
    IPropertyRepository propertyRepository,
    IRoomAssetRepository roomAssetRepository,
    IRoomAssetConditionHistoryRepository roomAssetConditionHistoryRepository,
    ILeaseRepository leaseRepository,
    IMediaCollectionService mediaCollectionService,
    IStorageProvider storageProvider,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentOrganizationAccessor currentOrganizationAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<RoomService> logger) : IRoomService
{
    private static readonly Dictionary<string, Expression<Func<Room, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = r => r.RoomNumber,
        ["roomNumber"] = r => r.RoomNumber,
        ["createdAt"] = r => r.CreatedAt,
        ["roomStatus"] = r => r.RoomStatus
    };

    public async Task<PagedResult<RoomResponse>> GetAllAsync(RoomListQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = query.OrganizationId ?? currentOrganizationAccessor.OrganizationId;

        var scope = await organizationScopeAccessor.GetScopeAsync(PermissionResources.Room, PermissionActions.Read, cancellationToken);
        if (organizationId is not null && !scope.Allows(organizationId.Value))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId.Value));
        }

        var allowedOrganizationIds = organizationId is null && !scope.IsUnrestricted ? scope.OrganizationIds : null;

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await roomRepository.SearchAsync(
            organizationId, allowedOrganizationIds, query.PropertyId, query.RoomCategory, query.RoomStatus,
            query.Search, query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<RoomResponse>
        {
            Items = paged.Items
                .Select(r => r.Entity.Adapt<RoomResponse>() with
                {
                    CoverUrl = r.Media is null ? null : storageProvider.GetUrl(r.Media.StorageKey, r.Media.MimeType)
                })
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<RoomDetailsResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await roomRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Room(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, PermissionResources.Room, PermissionActions.Read, NotFoundMessages.Room(id), cancellationToken);

        return await ToDetailsResponseAsync(room, cancellationToken);
    }

    public async Task<RoomDetailsResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var room = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // FOR SHARE blocks a concurrent PropertyService.DeleteAsync (FOR UPDATE) until this insert commits
                var property = await propertyRepository.GetByIdForShareAsync(request.PropertyId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Property(request.PropertyId));

                await organizationScopeAccessor.EnsureScopeAsync(
                    property.OrganizationId, PermissionResources.Room, PermissionActions.Create, NotFoundMessages.Property(request.PropertyId), ct);

                var created = Room.Create(
                    request.PropertyId,
                    new RoomDetails(
                        request.RoomNumber,
                        request.RoomCategory,
                        request.FloorNumber,
                        request.Area,
                        request.Capacity,
                        request.MonthlyRent,
                        request.DepositAmount,
                        request.Note),
                    request.Amenities?
                        .Select(a => new AmenityChange
                        {
                            Name = a.Name,
                            Quantity = a.Quantity,
                            Icon = a.Icon
                        }).ToList() ?? [],
                    currentUserAccessor.RequiredUser.Id);

                await roomRepository.AddAsync(created, ct);
                return created;
            }, cancellationToken);

            logger.LogInformation("Room created ({RoomId})", room.Id);
            return room.Adapt<RoomDetailsResponse>() with { Assets = [], Media = [] };
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.RoomPropertyIdRoomNumber)
        {
            logger.LogWarning("Create room failed: room number already in use in property ({PropertyId})", request.PropertyId);
            throw new AppConflictException("A room with this number already exists in the property");
        }
    }

    public async Task<RoomDetailsResponse> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var room = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var (existing, organizationId) = await roomRepository.GetAggregateForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Room(id));

                await organizationScopeAccessor.EnsureScopeAsync(
                    organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.Room(id), ct);

                if (request.RoomStatus.IsSet && request.RoomStatus.Value != existing.RoomStatus)
                {
                    existing.ChangeStatus(
                        request.RoomStatus.Value,
                        hasActiveLease: await leaseRepository.ExistsActiveByRoomIdAsync(id, ct));
                }

                if (request.Capacity is { IsSet: true, Value: { } capacity } && capacity < (existing.Capacity ?? int.MaxValue))
                {
                    var currentTenants = await leaseRepository.CountCurrentTenantsOfActiveLeaseByRoomIdAsync(id, ct);
                    if (capacity < currentTenants)
                    {
                        throw new AppConflictException(
                            $"Capacity cannot be lower than the {currentTenants} current tenants of the room's active lease");
                    }
                }

                var current = existing.Details;
                existing.Update(new RoomDetails(
                    request.RoomNumber.OrKeep(current.RoomNumber)!,
                    request.RoomCategory.OrKeep(current.RoomCategory),
                    request.FloorNumber.OrKeep(current.FloorNumber),
                    request.Area.OrKeep(current.Area),
                    request.Capacity.OrKeep(current.Capacity),
                    request.MonthlyRent.OrKeep(current.MonthlyRent),
                    request.DepositAmount.OrKeep(current.DepositAmount),
                    request.Note.OrKeep(current.Note)));

                if (request.Amenities.IsSet)
                {
                    existing.ApplyAmenityChanges(
                        request.Amenities.Value!.Select(ToAmenityChange).ToList(),
                        currentUserAccessor.RequiredUser.Id);
                }

                return existing;
            }, cancellationToken);

            logger.LogInformation("Room updated ({RoomId})", room.Id);

            return await ToDetailsResponseAsync(room, cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.RoomPropertyIdRoomNumber)
        {
            logger.LogWarning("Update room failed: room number already in use in property ({RoomId})", id);
            throw new AppConflictException("A room with this number already exists in the property");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removedMedia = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (room, organizationId) = await roomRepository.GetAggregateForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Room(id));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId, PermissionResources.Room, PermissionActions.Delete, NotFoundMessages.Room(id), ct);

            if (await leaseRepository.ExistsOpenByRoomIdAsync(id, ct))
            {
                logger.LogWarning("Delete room failed: room still has a draft or active lease ({RoomId})", id);
                throw new AppConflictException("Cannot delete a room that has a draft or active lease");
            }

            var assets = await roomAssetRepository.ListByRoomIdForUpdateAsync(id, ct);
            foreach (var asset in assets)
            {
                roomAssetRepository.SoftDelete(asset);
            }

            var mediaAssets = room.Media.Select(m => m.MediaAsset!).ToList();
            var histories = await roomAssetConditionHistoryRepository.ListWithPhotosByAssetIdsAsync(
                assets.Select(a => a.Id).ToList(), ct);
            mediaAssets.AddRange(histories.SelectMany(h => h.RemoveAllPhotos()));

            roomRepository.SoftDelete(room);
            mediaCollectionService.StageRemove(mediaAssets);

            return mediaAssets;
        }, cancellationToken);

        await mediaCollectionService.CompleteAsync(removedMedia);

        logger.LogInformation("Room soft-deleted ({RoomId})", id);
    }

    public async Task<List<RoomMediaResponse>> AddMediaAsync(
        Guid roomId, IReadOnlyList<IFileUpload> files, CancellationToken cancellationToken = default)
    {
        var room = await roomRepository.GetByIdWithDetailsAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

        var organizationId = room.Property!.OrganizationId;
        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.Room(roomId), cancellationToken);

        room.EnsureCanAddMedia(files.Count);

        var media = await mediaCollectionService.AddAsync(
            MediaAssetEntityType.RoomMedia,
            files,
            $"organizations/{organizationId}/rooms/{roomId}",
            organizationId,
            async (uploaded, ct) =>
            {
                var locked = await LockRoomForMediaChangeAsync(roomId, ct);

                locked.AddMedia(uploaded, currentUserAccessor.RequiredUser.Id);

                return locked.Media.ToList();
            },
            cancellationToken);

        logger.LogInformation("Room media added ({RoomId}, {Count})", roomId, files.Count);
        return media.ToMediaResponses(storageProvider);
    }

    public async Task<List<RoomMediaResponse>> UpdateMediaAsync(
        Guid roomId, UpdateRoomMediaRequest request, CancellationToken cancellationToken = default)
    {
        var media = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var room = await LockRoomForMediaChangeAsync(roomId, ct);

            if (request.Order.IsSet)
            {
                room.ReorderMedia(request.Order.Value!);
            }

            if (request.CoverMediaId is { IsSet: true, Value: { } coverMediaId })
            {
                room.ReleaseCoverFor(coverMediaId);
                await unitOfWork.SaveChangesAsync(ct);
                room.SetCover(coverMediaId);
            }

            return room.Media.ToList();
        }, cancellationToken);

        logger.LogInformation("Room media updated ({RoomId})", roomId);
        return media.ToMediaResponses(storageProvider);
    }

    public async Task DeleteMediaAsync(Guid roomId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var removed = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var room = await LockRoomForMediaChangeAsync(roomId, ct);

            var target = room.RemoveMedia(mediaId);
            mediaCollectionService.StageRemove([target.MediaAsset!]);

            if (room.HasPendingCoverChange)
            {
                await unitOfWork.SaveChangesAsync(ct);
                room.ApplyPendingCoverChange();
            }

            return target.MediaAsset!;
        }, cancellationToken);

        await mediaCollectionService.CompleteAsync([removed]);

        logger.LogInformation("Room media removed ({RoomId}, {MediaAssetId})", roomId, mediaId);
    }

    private static AmenityChange ToAmenityChange(UpdateRoomAmenityRequest request) => new()
    {
        Id = request.Id,
        Name = request.Name,
        Quantity = request.Quantity.ToChange(),
        Icon = request.Icon.ToChange()!,
        IsDeleted = request.IsDeleted
    };

    private async Task<Room> LockRoomForMediaChangeAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var (room, organizationId) = await roomRepository.GetAggregateForUpdateAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.Room, PermissionActions.Update, NotFoundMessages.Room(roomId), cancellationToken);

        return room;
    }

    private async Task<RoomDetailsResponse> ToDetailsResponseAsync(Room room, CancellationToken cancellationToken)
    {
        var assets = await roomAssetRepository.ListByRoomIdAsync(room.Id, cancellationToken);
        var currentPhotos = await roomAssetConditionHistoryRepository.ListCurrentPhotosByAssetIdsAsync(
            assets.Select(a => a.Id).ToList(), cancellationToken);
        var media = room.Media.ToMediaResponses(storageProvider);

        return room.Adapt<RoomDetailsResponse>() with
        {
            Media = media,
            CoverUrl = media.FirstOrDefault(m => m.IsCover)?.Url,
            Assets = assets
                .Select(a => currentPhotos.TryGetValue(a.Id, out var photos)
                    ? a.Adapt<RoomDetailsAssetResponse>() with
                    {
                        CurrentPhotos = photos.History.ToCurrentPhotosResponse(photos.IsLatest, storageProvider)
                    }
                    : a.Adapt<RoomDetailsAssetResponse>())
                .ToList()
        };
    }
}
