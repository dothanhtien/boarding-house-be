using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Configurations;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Storage;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class RoomService(
    IRoomRepository roomRepository,
    IPropertyRepository propertyRepository,
    IRoomAssetRepository roomAssetRepository,
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

        var scope = await organizationScopeAccessor.GetScopeAsync("room", "read", cancellationToken);
        if (organizationId is not null && !scope.Includes(organizationId.Value))
        {
            throw new AppNotFoundException($"Organization '{organizationId}' not found");
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
            ?? throw new AppNotFoundException($"Room '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "read", $"Room '{id}' not found", cancellationToken);

        return ToDetailsResponse(room);
    }

    public async Task<RoomDetailsResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var room = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // FOR SHARE blocks a concurrent PropertyService.DeleteAsync (FOR UPDATE) until this insert commits
                var property = await propertyRepository.GetByIdForShareAsync(request.PropertyId, ct)
                    ?? throw new AppNotFoundException($"Property '{request.PropertyId}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    property.OrganizationId, "room", "create", $"Property '{request.PropertyId}' not found", ct);

                var created = new Room
                {
                    PropertyId = request.PropertyId,
                    RoomNumber = request.RoomNumber,
                    RoomCategory = request.RoomCategory,
                    FloorNumber = request.FloorNumber,
                    Area = request.Area,
                    Capacity = request.Capacity,
                    MonthlyRent = request.MonthlyRent,
                    DepositAmount = request.DepositAmount,
                    Note = request.Note,
                    CreatedBy = currentUserAccessor.RequiredUser.Id,
                    Amenities = request.Amenities?
                        .Select(a => new RoomAmenity
                        {
                            Name = a.Name,
                            Quantity = a.Quantity,
                            Icon = a.Icon,
                            CreatedBy = currentUserAccessor.RequiredUser.Id
                        })
                        .ToList() ?? []
                };

                await roomRepository.AddAsync(created, ct);
                return created;
            }, cancellationToken);

            logger.LogInformation("Room created ({RoomId})", room.Id);
            return room.Adapt<RoomDetailsResponse>() with { Media = [] };
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(RoomConfiguration.PropertyIdRoomNumberUniqueIndex))
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
                var existing = await roomRepository.GetByIdWithPropertyForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException($"Room '{id}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    existing.Property!.OrganizationId, "room", "update", $"Room '{id}' not found", ct);

                await roomRepository.LoadAmenitiesAsync(existing, ct);

                request.Adapt(existing, RoomMappingConfig.UpdateConfig);

                if (request.Amenities.IsSet)
                {
                    var changes = request.Amenities.Value!;
                    EnsureAmenityChangesValid(existing, changes);
                    RemoveDeletedAmenities(existing, changes);
                    UpsertAmenities(existing, changes);
                }

                roomRepository.Update(existing);
                await roomRepository.LoadAssetsAsync(existing, ct);
                await roomRepository.LoadMediaAsync(existing, ct);

                return existing;
            }, cancellationToken);

            logger.LogInformation("Room updated ({RoomId})", room.Id);
            return ToDetailsResponse(room);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(RoomConfiguration.PropertyIdRoomNumberUniqueIndex))
        {
            logger.LogWarning("Update room failed: room number already in use in property ({RoomId})", id);
            throw new AppConflictException("A room with this number already exists in the property");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removedMedia = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var room = await roomRepository.GetByIdWithPropertyForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException($"Room '{id}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                room.Property!.OrganizationId, "room", "delete", $"Room '{id}' not found", ct);

            // Amenities must be tracked so EF cascades the room's soft-delete to them
            await roomRepository.LoadAmenitiesAsync(room, ct);
            await roomRepository.LoadMediaAsync(room, ct);

            var assets = await roomAssetRepository.ListByRoomIdForUpdateAsync(id, ct);
            foreach (var asset in assets)
            {
                roomAssetRepository.SoftDelete(asset);
            }

            var mediaAssets = room.Media.Select(m => m.MediaAsset!).ToList();

            roomRepository.SoftDelete(room);
            mediaCollectionService.StageRemove(mediaAssets);

            return mediaAssets;
        }, cancellationToken);

        await mediaCollectionService.CompleteAsync(removedMedia);

        logger.LogInformation("Room soft-deleted ({RoomId})", id);
    }

    public async Task<List<RoomMediaResponse>> AddMediaAsync(
        Guid roomId, AddRoomMediaRequest request, CancellationToken cancellationToken = default)
    {
        var files = request.Files!;

        var room = await roomRepository.GetByIdWithDetailsAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{roomId}' not found");

        var organizationId = room.Property!.OrganizationId;
        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, "room", "update", $"Room '{roomId}' not found", cancellationToken);

        EnsureMediaLimit(room, files.Count);

        var uploaded = await mediaCollectionService.UploadAsync(
            MediaAssetEntityType.RoomMedia, files, $"organizations/{organizationId}/rooms/{roomId}", organizationId,
            cancellationToken);

        List<RoomMedia> media;
        try
        {
            media = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var locked = await LockRoomForMediaChangeAsync(roomId, ct);

                EnsureMediaLimit(locked, uploaded.Count);

                await mediaCollectionService.StageAddAsync(uploaded, ct);

                var nextSortOrder = locked.Media.Count == 0 ? 0 : locked.Media.Max(m => m.SortOrder) + 1;
                var needsCover = !locked.Media.Any(m => m.IsCover);
                var userId = currentUserAccessor.RequiredUser.Id;

                for (var i = 0; i < uploaded.Count; i++)
                {
                    roomRepository.AddMedia(locked, new RoomMedia
                    {
                        RoomId = locked.Id,
                        MediaAssetId = uploaded[i].Id,
                        MediaAsset = uploaded[i],
                        SortOrder = nextSortOrder + i,
                        IsCover = needsCover && i == 0,
                        CreatedBy = userId
                    });
                }

                return locked.Media.ToList();
            }, cancellationToken);
        }
        catch
        {
            await mediaCollectionService.DiscardAsync(uploaded);
            throw;
        }

        logger.LogInformation("Room media added ({RoomId}, {Count})", roomId, uploaded.Count);
        return ToMediaResponses(media);
    }

    public async Task<List<RoomMediaResponse>> UpdateMediaAsync(
        Guid roomId, UpdateRoomMediaRequest request, CancellationToken cancellationToken = default)
    {
        var media = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var room = await LockRoomForMediaChangeAsync(roomId, ct);

            if (request.Order.IsSet)
            {
                ApplyMediaOrder(room, request.Order.Value!);
            }

            if (request.CoverMediaId.IsSet)
            {
                await ChangeCoverAsync(room, request.CoverMediaId.Value!.Value, ct);
            }

            return room.Media.ToList();
        }, cancellationToken);

        logger.LogInformation("Room media updated ({RoomId})", roomId);
        return ToMediaResponses(media);
    }

    public async Task DeleteMediaAsync(Guid roomId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var removed = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var room = await LockRoomForMediaChangeAsync(roomId, ct);

            var target = room.Media.FirstOrDefault(m => m.MediaAssetId == mediaId)
                ?? throw new AppNotFoundException($"Room media '{mediaId}' not found");

            roomRepository.RemoveMedia(room, target);
            mediaCollectionService.StageRemove([target.MediaAsset!]);

            var nextCover = target.IsCover ? room.Media.OrderBy(m => m.SortOrder).FirstOrDefault() : null;
            if (nextCover is not null)
            {
                await unitOfWork.SaveChangesAsync(ct);
                nextCover.IsCover = true;
            }

            return target.MediaAsset!;
        }, cancellationToken);

        await mediaCollectionService.CompleteAsync([removed]);

        logger.LogInformation("Room media removed ({RoomId}, {MediaAssetId})", roomId, mediaId);
    }

    // Validates the merge against the room's current amenities: every Id must belong to this room, and the
    // resulting list must stay within the per-room limit.
    private static void EnsureAmenityChangesValid(Room room, List<UpdateRoomAmenityRequest> changes)
    {
        var existingById = room.Amenities.ToDictionary(a => a.Id);
        var unknown = changes.FirstOrDefault(c => c.Id is not null && !existingById.ContainsKey(c.Id.Value));
        if (unknown is not null)
        {
            throw new AppNotFoundException($"Room amenity '{unknown.Id}' not found");
        }

        var deletedCount = changes.Count(c => c.IsDeleted);
        var addedCount = changes.Count(c => c.Id is null);
        if (room.Amenities.Count - deletedCount + addedCount > Room.MaxAmenities)
        {
            throw new AppValidationException($"A room must not have more than {Room.MaxAmenities} amenities");
        }
    }

    // Removing from the collection orphans the amenity → EF marks it Deleted → the interceptor soft-deletes it.
    private static void RemoveDeletedAmenities(Room room, List<UpdateRoomAmenityRequest> changes)
    {
        var deletedIds = changes.Where(c => c.IsDeleted).Select(c => c.Id!.Value).ToHashSet();
        var deleted = room.Amenities.Where(a => deletedIds.Contains(a.Id)).ToList();
        foreach (var amenity in deleted)
        {
            room.Amenities.Remove(amenity);
        }
    }

    private void UpsertAmenities(Room room, List<UpdateRoomAmenityRequest> changes)
    {
        var existingById = room.Amenities.ToDictionary(a => a.Id);
        foreach (var change in changes.Where(c => !c.IsDeleted))
        {
            if (change.Id is { } amenityId)
            {
                var existing = existingById[amenityId];
                if (change.Name is not null)
                {
                    existing.Name = change.Name;
                }

                change.Quantity.ApplyIfSet(quantity => existing.Quantity = quantity);
                change.Icon.ApplyIfSet(icon => existing.Icon = icon);
            }
            else
            {
                roomRepository.AddAmenity(room, new RoomAmenity
                {
                    RoomId = room.Id,
                    Name = change.Name!,
                    Quantity = change.Quantity.Value,
                    Icon = change.Icon.Value,
                    CreatedBy = currentUserAccessor.RequiredUser.Id
                });
            }
        }
    }

    private async Task<Room> LockRoomForMediaChangeAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await roomRepository.GetByIdWithPropertyForUpdateAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{roomId}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "update", $"Room '{roomId}' not found", cancellationToken);

        await roomRepository.LoadMediaAsync(room, cancellationToken);

        return room;
    }

    private static void EnsureMediaLimit(Room room, int addedCount)
    {
        if (room.Media.Count + addedCount > Room.MaxMedia)
        {
            throw new AppValidationException($"A room must not have more than {Room.MaxMedia} media");
        }
    }

    private static void ApplyMediaOrder(Room room, List<Guid> order)
    {
        var mediaById = room.Media.ToDictionary(m => m.MediaAssetId);
        if (order.Count != mediaById.Count || !order.All(mediaById.ContainsKey))
        {
            throw new AppValidationException("Order must list every media of the room exactly once");
        }

        for (var i = 0; i < order.Count; i++)
        {
            mediaById[order[i]].SortOrder = i;
        }
    }

    private async Task ChangeCoverAsync(Room room, Guid coverMediaId, CancellationToken cancellationToken)
    {
        var cover = room.Media.FirstOrDefault(m => m.MediaAssetId == coverMediaId)
            ?? throw new AppNotFoundException($"Room media '{coverMediaId}' not found");

        var previous = room.Media.FirstOrDefault(m => m.IsCover);
        if (previous == cover)
        {
            return;
        }

        if (previous is not null)
        {
            previous.IsCover = false;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        cover.IsCover = true;
    }

    private RoomDetailsResponse ToDetailsResponse(Room room)
    {
        var media = ToMediaResponses(room.Media);
        return room.Adapt<RoomDetailsResponse>() with
        {
            Media = media,
            CoverUrl = media.FirstOrDefault(m => m.IsCover)?.Url
        };
    }

    private List<RoomMediaResponse> ToMediaResponses(IEnumerable<RoomMedia> media) =>
        media
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.SortOrder)
            .Select(m => new RoomMediaResponse
            {
                MediaId = m.MediaAssetId,
                Url = storageProvider.GetUrl(m.MediaAsset!.StorageKey, m.MediaAsset.MimeType),
                MimeType = m.MediaAsset.MimeType,
                FileName = m.MediaAsset.FileName,
                SortOrder = m.SortOrder,
                IsCover = m.IsCover,
                CreatedAt = m.CreatedAt
            })
            .ToList();
}
