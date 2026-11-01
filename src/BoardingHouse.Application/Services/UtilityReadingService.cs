using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using Mapster;

namespace BoardingHouse.Application.Services;

public class UtilityReadingService(
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
    public async Task<UtilityReadingRoomMetersResponse> RecordAsync(
        Guid roomId, RecordRoomUtilityReadingsRequest request, CancellationToken cancellationToken = default)
    {
        var readOn = request.ReadOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var readingMonth = request.ReadingMonth!.Value;
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

                // The room is locked, so its active lease cannot change before the commit
                var activeLease = await leaseRepository.GetActiveByRoomIdAsync(lockedRoom.Id, ct);

                // All-or-nothing: collect every invalid item (keyed like FluentValidation errors) before failing, so the
                // request can be fixed and resent in one go
                var errors = new Dictionary<string, string[]>();
                var readings = new List<UtilityReading>();
                var now = DateTimeOffset.UtcNow;
                var createdBy = currentUserAccessor.RequiredUser.Id;

                foreach (var (item, index) in request.Items.Select((item, index) => (item, index)))
                {
                    if (!serviceIds.Contains(item.ServiceId))
                    {
                        errors[$"items[{index}]"] = ["The room has no metered utility service with this id"];
                        continue;
                    }

                    var latest = latestByService.GetValueOrDefault(item.ServiceId);
                    if (latest is not null && latest.ReadOn > readOn)
                    {
                        errors[$"items[{index}]"] = [$"A later reading ({latest.ReadOn:yyyy-MM-dd}) already exists on this meter"];
                        continue;
                    }

                    if (latest is not null && latest.ReadingMonth >= readingMonth)
                    {
                        errors[$"items[{index}]"] = latest.ReadingMonth == readingMonth
                            ? ["A reading already exists for this month — delete it to enter it again"]
                            : [$"A reading for a later month ({latest.ReadingMonth:yyyy-MM}) already exists"];
                        continue;
                    }

                    var previousValue = latest?.CurrentValue ?? item.PreviousValue;
                    if (item.CurrentValue < previousValue)
                    {
                        errors[$"items[{index}].{nameof(item.CurrentValue)}"] =
                            [$"CurrentValue must not be less than the previous reading ({previousValue})"];
                        continue;
                    }

                    readings.Add(new UtilityReading
                    {
                        RoomId = lockedRoom.Id,
                        ServiceId = item.ServiceId,
                        ReadOn = readOn,
                        ReadingMonth = readingMonth,
                        BilledLeaseId = activeLease?.Id,
                        PreviousValue = previousValue,
                        CurrentValue = item.CurrentValue,
                        CreatedAt = now,
                        CreatedBy = createdBy
                    });
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
            when (ex.ConstraintName == UniqueIndexNames.UtilityReadingRoomIdServiceIdReadingMonth)
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

    private async Task<UtilityReadingRoomMetersResponse> BuildRoomMetersResponseAsync(
        Room room,
        IReadOnlyList<UtilityService> services,
        Lease? lease,
        IReadOnlyDictionary<Guid, UtilityReading> latestReadings,
        DateOnly readingMonth,
        CancellationToken cancellationToken)
    {
        var monthReadings = (await utilityReadingRepository.ListByRoomAndMonthWithImageAsync(room.Id, readingMonth, cancellationToken))
            .ToDictionary(r => r.Entity.ServiceId);

        var leaseIds = monthReadings.Values
            .Select(r => r.Entity.BilledLeaseId)
            .Append(lease?.Id)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var leaseServices = (await leaseUtilityServiceRepository.ListActiveByLeaseIdsAsync(leaseIds, cancellationToken))
            .ToDictionary(s => (s.LeaseId, s.ServiceId));

        var tenantCount = lease is null
            ? 0
            : await leaseRepository.CountCurrentTenantsOfActiveLeaseByRoomIdAsync(room.Id, cancellationToken);

        var meterResponses = services
            .Select(service =>
            {
                var reading = monthReadings.GetValueOrDefault(service.Id);
                var billedLeaseId = reading is null ? lease?.Id : reading.Entity.BilledLeaseId;

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
                    EffectiveUnitPrice = billedLeaseId is null
                        ? null
                        : leaseServices.GetValueOrDefault((billedLeaseId.Value, service.Id))?.EffectiveUnitPrice
                };
            })
            .ToList();

        return new UtilityReadingRoomMetersResponse
        {
            RoomId = room.Id,
            RoomNumber = room.RoomNumber,
            RoomStatus = room.RoomStatus,
            ReadingMonth = readingMonth,
            Lease = lease is null
                ? null
                : new UtilityReadingRoomLeaseResponse { LeaseId = lease.Id, LeaseCode = lease.LeaseCode, Status = lease.Status },
            Meters = meterResponses,
            OtherCharges = lease is null
                ? []
                : leaseServices.Values
                    .Where(s => s.LeaseId == lease.Id && s.Service!.BillingMethod != UtilityBillingMethod.Metered)
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
