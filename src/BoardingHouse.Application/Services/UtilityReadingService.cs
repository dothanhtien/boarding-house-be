using System.Linq.Expressions;
using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.Services;

public class UtilityReadingService(
    IPropertyRepository propertyRepository,
    IUtilityServiceRepository utilityServiceRepository,
    IRoomRepository roomRepository,
    ILeaseRepository leaseRepository,
    ILeaseUtilityServiceRepository leaseUtilityServiceRepository,
    IUtilityReadingRepository utilityReadingRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IOrganizationScopeAccessor organizationScopeAccessor,
    IStorageProvider storageProvider,
    ILogger<UtilityReadingService> logger) : IUtilityReadingService
{
    private static readonly Dictionary<string, Expression<Func<RoomWithRecordedMeterCount, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = r => r.Room.RoomNumber,
        ["roomNumber"] = r => r.Room.RoomNumber
    };

    public async Task<PagedResult<UtilityReadingRoomListItemResponse>> GetRoomsAsync(
        Guid propertyId, UtilityReadingRoomListQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = await propertyRepository.GetOrganizationIdAsync(propertyId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Property(propertyId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.UtilityReading,
            PermissionActions.Read,
            NotFoundMessages.Property(propertyId),
            cancellationToken);

        var readingMonth = BillingMonth.FromFirstDay(query.ReadingMonth!.Value);

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await roomRepository.SearchForUtilityReadingAsync(
            propertyId,
            readingMonth,
            query.Recorded,
            query.Search,
            query,
            sortField,
            query.SortOrder,
            cancellationToken);

        var roomIds = paged.Items.Select(r => r.Room.Id).ToList();
        var activeLeases = (await leaseRepository.ListActiveOfMonthByRoomIdsAsync(roomIds, readingMonth, cancellationToken))
            .ToDictionary(l => l.RoomId);
        var lastBilledReadings = (await utilityReadingRepository.ListLastBilledLeasesOfMonthByRoomIdsAsync(roomIds, readingMonth, cancellationToken))
            .ToDictionary(r => r.RoomId);

        return new PagedResult<UtilityReadingRoomListItemResponse>
        {
            Items = paged.Items
                .Select(r => new UtilityReadingRoomListItemResponse
                {
                    RoomId = r.Room.Id,
                    RoomNumber = r.Room.RoomNumber,
                    RoomStatus = r.Room.RoomStatus,
                    Lease = (lastBilledReadings.GetValueOrDefault(r.Room.Id)
                            ?? activeLeases.GetValueOrDefault(r.Room.Id))
                        ?.Adapt<UtilityReadingRoomLeaseResponse>(),
                    MeterCount = r.MeterCount,
                    RecordedCount = r.RecordedCount,
                    Status = r.MeterCount == 0 ? UtilityReadingRoomStatus.NoMeters
                        : r.RecordedCount == r.MeterCount ? UtilityReadingRoomStatus.Recorded
                        : r.RecordedCount == 0 ? UtilityReadingRoomStatus.Pending
                        : UtilityReadingRoomStatus.Partial
                })
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<UtilityReadingRoomMetersResponse> GetRoomMetersAsync(
        Guid roomId, UtilityReadingRoomMetersQuery query, CancellationToken cancellationToken = default)
    {
        var (room, organizationId) = await roomRepository.GetByIdWithOrganizationIdAsync(roomId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.UtilityReading,
            PermissionActions.Read,
            NotFoundMessages.Room(roomId),
            cancellationToken);

        var readingMonth = BillingMonth.FromFirstDay(query.ReadingMonth!.Value);
        var services = await utilityServiceRepository.ListMeteredServicesByPropertyIdAsync(room.PropertyId, cancellationToken);
        var latestByService = (await utilityReadingRepository.ListLatestByRoomIdAsync(room.Id, cancellationToken))
            .ToDictionary(r => r.ServiceId);
        var activeLease = await leaseRepository.GetActiveOfMonthByRoomIdAsync(room.Id, readingMonth, cancellationToken);

        return await BuildRoomMetersResponseAsync(
            room, services, activeLease, latestByService, readingMonth, cancellationToken);
    }

    public async Task<UtilityReadingRoomMetersResponse> RecordAsync(
        Guid roomId, RecordRoomUtilityReadingsRequest request, CancellationToken cancellationToken = default)
    {
        var readOn = request.ReadOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var readingMonth = BillingMonth.FromFirstDay(request.ReadingMonth!.Value);
        Room room;
        List<UtilityService> services;
        Lease? lease;
        Dictionary<Guid, UtilityReading> latestReadings;

        try
        {
            (room, services, lease, latestReadings) = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // FOR UPDATE serializes every write on this room's meters (other record requests, reading deletes) and
                // lease status transitions of the room (they lock the room first), so the chain checks below stay true
                var (lockedRoom, organizationId) = await roomRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

                await organizationScopeAccessor.EnsureScopeAsync(
                    organizationId,
                    PermissionResources.UtilityReading,
                    PermissionActions.Create,
                    NotFoundMessages.Room(roomId),
                    ct);

                var meteredServices = await utilityServiceRepository.ListMeteredServicesByPropertyIdAsync(lockedRoom.PropertyId, ct);
                var serviceIds = meteredServices.Select(s => s.Id).ToHashSet();

                var latestByService = (await utilityReadingRepository.ListLatestByRoomIdAsync(lockedRoom.Id, ct))
                    .ToDictionary(r => r.ServiceId);

                var activeLease = await leaseRepository.GetActiveOfMonthByRoomIdAsync(lockedRoom.Id, readingMonth, ct);

                var errors = new Dictionary<string, string[]>();
                var readings = new List<UtilityReading>();
                var now = DateTimeOffset.UtcNow;
                var createdBy = currentUserAccessor.RequiredUser.Id;

                foreach (var (item, index) in request.Items.Select((item, index) => (item, index)))
                {
                    var itemKey = $"items[{index}]";

                    if (!serviceIds.Contains(item.ServiceId))
                    {
                        errors[itemKey] = ["The room has no metered utility service with this id"];
                        continue;
                    }

                    try
                    {
                        readings.Add(UtilityReading.Record(
                            lockedRoom.Id,
                            item.ServiceId,
                            readOn,
                            readingMonth,
                            latestByService.GetValueOrDefault(item.ServiceId),
                            item.PreviousValue,
                            item.CurrentValue,
                            activeLease?.Id,
                            now,
                            createdBy));
                    }
                    catch (DomainException ex)
                    {
                        errors[ex.Field is null ? itemKey : $"{itemKey}.{ex.Field}"] = [ex.Message];
                    }
                }

                if (errors.Count > 0)
                {
                    throw new AppValidationException("One or more readings are invalid", errors);
                }

                await utilityReadingRepository.AddRangeAsync(readings, ct);

                // The new readings become the latest of their meters without a second query: ReadOn >= the previous
                // latest's ReadOn and CreatedAt is later, matching ListLatestByRoomIdAsync's ordering; Id is filled from
                // RETURNING on commit. The summary doesn't carry Consumption, so the tracked entities are enough
                foreach (var reading in readings)
                {
                    latestByService[reading.ServiceId] = reading;
                }

                return (lockedRoom, meteredServices, activeLease, latestByService);
            }, cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == UniqueIndexNames.UtilityReadingRoomIdReadingMonthServiceId)
        {
            logger.LogWarning(
                "Record utility readings failed: readings recorded concurrently (room {RoomId}, month {ReadingMonth})",
                roomId, readingMonth);
            throw new AppConflictException("Readings for this month were recorded by another request, please reload");
        }

        logger.LogInformation(
            "Utility readings recorded (room {RoomId}, month {ReadingMonth}, {Count} readings)",
            roomId, readingMonth, request.Items.Count);

        return await BuildRoomMetersResponseAsync(room, services, lease, latestReadings, readingMonth, cancellationToken);
    }

    public async Task DeleteAsync(Guid roomId, Guid readingId, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (_, organizationId) = await roomRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Room(roomId));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId,
                PermissionResources.UtilityReading,
                PermissionActions.Delete,
                NotFoundMessages.Room(roomId),
                ct);

            var reading = await utilityReadingRepository.GetByIdAsync(roomId, readingId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.UtilityReading(readingId));

            if (await utilityReadingRepository.GetLatestIdOfMeterAsync(roomId, reading.ServiceId, ct) != reading.Id)
            {
                throw new AppConflictException("Only the latest reading of a meter can be deleted");
            }

            utilityReadingRepository.SoftDelete(reading);
        }, cancellationToken);

        logger.LogInformation("Utility reading deleted (room {RoomId}, reading {UtilityReadingId})", roomId, readingId);
    }

    private static bool IsActive(LeaseUtilityService leaseService) =>
        leaseService.IsActive && leaseService.Service!.IsActive && !leaseService.Service.IsDeleted;

    private async Task<UtilityReadingRoomMetersResponse> BuildRoomMetersResponseAsync(
        Room room,
        IReadOnlyList<UtilityService> services,
        Lease? activeLease,
        IReadOnlyDictionary<Guid, UtilityReading> latestReadings,
        BillingMonth readingMonth,
        CancellationToken cancellationToken)
    {
        var monthReadings = (await utilityReadingRepository.ListByRoomAndMonthWithImageAsync(room.Id, readingMonth, cancellationToken))
            .ToDictionary(r => r.Entity.ServiceId);
        var lastBilledReading = (await utilityReadingRepository.ListLastBilledLeasesOfMonthByRoomIdsAsync([room.Id], readingMonth, cancellationToken))
            .SingleOrDefault();
        var lease = lastBilledReading ?? activeLease;

        var leaseIds = monthReadings.Values
            .Select(r => r.Entity.BilledLeaseId)
            .Append(activeLease?.Id)
            .Append(lease?.Id)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var leaseServices = (await leaseUtilityServiceRepository.ListByLeaseIdsIncludingInactiveAsync(leaseIds, cancellationToken))
            .ToDictionary(s => (s.LeaseId, s.ServiceId));

        var tenantCount = lease is null
            ? 0
            : await leaseRepository.CountTenantsInMonthAsync(lease.Id, readingMonth, cancellationToken);

        var meteredIds = services.Select(s => s.Id).ToHashSet();
        var meterServices = services
            .Concat(monthReadings.Values.Select(r => r.Entity.Service!).Where(s => !meteredIds.Contains(s.Id)))
            .OrderBy(s => s.Name)
            .ToList();
        var meterIds = meterServices.Select(s => s.Id).ToHashSet();

        var meterResponses = meterServices
            .Select(service =>
            {
                var reading = monthReadings.GetValueOrDefault(service.Id);
                var billedLeaseId = reading is null ? activeLease?.Id : reading.Entity.BilledLeaseId;
                var leaseService = billedLeaseId is null
                    ? null
                    : leaseServices.GetValueOrDefault((billedLeaseId.Value, service.Id));
                if (reading is null && leaseService is not null && !IsActive(leaseService))
                {
                    leaseService = null;
                }

                return new UtilityReadingRoomMeterResponse
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Unit = service.Unit,
                    Latest = latestReadings.GetValueOrDefault(service.Id)?.Adapt<UtilityReadingSummaryResponse>(),
                    Reading = reading is null
                        ? null
                        : reading.Entity.Adapt<UtilityReadingResponse>() with
                        {
                            ImageUrl = reading.Media is null ? null : storageProvider.GetUrl(reading.Media.StorageKey, reading.Media.MimeType)
                        },
                    BilledLeaseId = billedLeaseId,
                    EffectiveUnitPrice = leaseService?.EffectiveUnitPrice
                };
            })
            .ToList();

        return new UtilityReadingRoomMetersResponse
        {
            RoomId = room.Id,
            RoomNumber = room.RoomNumber,
            RoomStatus = room.RoomStatus,
            ReadingMonth = readingMonth.FirstDay,
            Lease = lease?.Adapt<UtilityReadingRoomLeaseResponse>(),
            Meters = meterResponses,
            OtherCharges = lease is null
                ? []
                : leaseServices.Values
                    .Where(s => s.LeaseId == lease.Id
                        && IsActive(s)
                        && s.Service!.BillingMethod != UtilityBillingMethod.Metered
                        && !meterIds.Contains(s.ServiceId))
                    .OrderBy(s => s.Service!.Name)
                    .Select(s =>
                    {
                        var quantity = s.Service!.BillingMethod == UtilityBillingMethod.PerTenant ? tenantCount : s.Quantity;

                        return new UtilityReadingRoomChargeResponse
                        {
                            ServiceId = s.ServiceId,
                            ServiceName = s.Service.Name,
                            BillingMethod = s.Service.BillingMethod,
                            Unit = s.Service.Unit,
                            UnitPrice = s.EffectiveUnitPrice,
                            Quantity = quantity,
                            Amount = s.EffectiveUnitPrice * quantity
                        };
                    })
                    .ToList()
        };
    }
}
