using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class RoomService(
    IRoomRepository roomRepository,
    IPropertyRepository propertyRepository,
    AppDbContext context,
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
        var room = await roomRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "read", $"Room '{id}' not found", cancellationToken);

        return room.Adapt<RoomResponse>();
    }

    public async Task<RoomResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var property = await propertyRepository.GetByIdForShareAsync(request.PropertyId, cancellationToken)
            ?? throw new AppNotFoundException($"Property '{request.PropertyId}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            property.OrganizationId, "room", "create", $"Property '{request.PropertyId}' not found", cancellationToken);

        var room = new Room
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
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        try
        {
            await roomRepository.AddAsync(room, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Create room failed: room number already in use in property ({PropertyId})", request.PropertyId);
            throw new AppConflictException("A room with this number already exists in the property");
        }

        logger.LogInformation("Room created ({RoomId})", room.Id);

        return room.Adapt<RoomResponse>();
    }

    public async Task<RoomResponse> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var room = await roomRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "update", $"Room '{id}' not found", cancellationToken);

        request.Adapt(room, RoomMappingConfig.UpdateConfig);

        try
        {
            roomRepository.Update(room);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Update room failed: room number already in use in property ({RoomId})", id);
            throw new AppConflictException("A room with this number already exists in the property");
        }

        logger.LogInformation("Room updated ({RoomId})", room.Id);

        return room.Adapt<RoomResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await roomRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Room '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            room.Property!.OrganizationId, "room", "delete", $"Room '{id}' not found", cancellationToken);

        roomRepository.SoftDelete(room);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Room soft-deleted ({RoomId})", room.Id);
    }
}
