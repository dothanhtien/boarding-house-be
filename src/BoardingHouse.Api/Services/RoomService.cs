using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Configurations;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class RoomService(
    IRoomRepository roomRepository,
    IPropertyRepository propertyRepository,
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
            Items = paged.Items.Adapt<List<RoomResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<RoomResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await roomRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "read", $"Room '{id}' not found", cancellationToken);

        return room.Adapt<RoomResponse>();
    }

    public async Task<RoomResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
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
            return room.Adapt<RoomResponse>();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(RoomConfiguration.PropertyIdRoomNumberUniqueIndex))
        {
            logger.LogWarning("Create room failed: room number already in use in property ({PropertyId})", request.PropertyId);
            throw new AppConflictException("A room with this number already exists in the property");
        }
    }

    public async Task<RoomResponse> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var room = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var existing = await roomRepository.GetByIdWithDetailsForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException($"Room '{id}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    existing.Property!.OrganizationId, "room", "update", $"Room '{id}' not found", ct);

                request.Adapt(existing, RoomMappingConfig.UpdateConfig);

                if (request.Amenities.IsSet)
                {
                    var changes = request.Amenities.Value!;
                    EnsureAmenityChangesValid(existing, changes);
                    RemoveDeletedAmenities(existing, changes);
                    UpsertAmenities(existing, changes);
                }

                roomRepository.Update(existing);

                return existing;
            }, cancellationToken);

            logger.LogInformation("Room updated ({RoomId})", room.Id);
            return room.Adapt<RoomResponse>();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(RoomConfiguration.PropertyIdRoomNumberUniqueIndex))
        {
            logger.LogWarning("Update room failed: room number already in use in property ({RoomId})", id);
            throw new AppConflictException("A room with this number already exists in the property");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await roomRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "delete", $"Room '{id}' not found", cancellationToken);

        roomRepository.SoftDelete(room);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Room soft-deleted ({RoomId})", room.Id);
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
}
