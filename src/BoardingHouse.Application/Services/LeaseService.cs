using System.Linq.Expressions;
using System.Security.Cryptography;
using Mapster;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Application.Services;

public class LeaseService(
    IUserRepository userRepository,
    IRoomRepository roomRepository,
    ILeaseRepository leaseRepository,
    IUtilityServiceRepository utilityServiceRepository,
    ILeaseStatusHistoryRepository leaseStatusHistoryRepository,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ICurrentOrganizationAccessor currentOrganizationAccessor,
    ILogger<LeaseService> logger) : ILeaseService
{
    private static readonly Dictionary<string, Expression<Func<Lease, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = l => l.StartDate,
        ["startDate"] = l => l.StartDate,
        ["createdAt"] = l => l.CreatedAt,
        ["leaseCode"] = l => l.LeaseCode
    };

    public async Task<PagedResult<LeaseResponse>> GetAllAsync(LeaseListQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = query.OrganizationId ?? currentOrganizationAccessor.OrganizationId;

        var scope = await organizationScopeAccessor.GetScopeAsync(
            PermissionResources.Lease,
            PermissionActions.Read,
            cancellationToken);

        if (organizationId is not null && !scope.Allows(organizationId.Value))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId.Value));
        }

        var allowedOrganizationIds = organizationId is null && !scope.IsUnrestricted ? scope.OrganizationIds : null;

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);

        var paged = await leaseRepository.SearchAsync(
            organizationId,
            allowedOrganizationIds,
            query.PropertyId,
            query.RoomId,
            query.Status,
            query.TenantUserId,
            query.Search,
            query,
            sortField,
            query.SortOrder,
            cancellationToken);

        return new PagedResult<LeaseResponse>
        {
            Items = paged.Items.Adapt<List<LeaseResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<LeaseResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lease = await leaseRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            lease.OrganizationId,
            PermissionResources.Lease,
            PermissionActions.Read,
            NotFoundMessages.Lease(id),
            cancellationToken
        );

        return lease.Adapt<LeaseResponse>();
    }

    public async Task<LeaseResponse> CreateAsync(CreateLeaseRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var lease = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var (room, organizationId) = await roomRepository.GetByIdWithOrganizationIdForShareAsync(request.RoomId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Room(request.RoomId));

                await organizationScopeAccessor.EnsureScopeAsync(
                    organizationId,
                    PermissionResources.Lease,
                    PermissionActions.Create,
                    NotFoundMessages.Room(request.RoomId),
                    ct);

                var monthlyRent = request.MonthlyRent ?? room.MonthlyRent
                    ?? throw new AppValidationException("MonthlyRent is required because the room has no listed rent");

                var depositAmount = request.DepositAmount ?? room.DepositAmount
                    ?? throw new AppValidationException("DepositAmount is required because the room has no listed deposit");

                if (request.PreviousLeaseId is { } previousLeaseId)
                {
                    await EnsureCanBePreviousLeaseAsync(previousLeaseId, organizationId, room.Id, leaseId: null, ct);
                }

                var tenants = request.Tenants ?? [];
                if (room.Capacity is { } capacity && tenants.Count > capacity)
                {
                    throw new AppValidationException($"The room allows at most {capacity} tenants");
                }

                var users = await EnsureUsersCanBeTenantsAsync(tenants.Select(t => t.UserId).ToList(), ct);
                var currentUserId = currentUserAccessor.RequiredUser.Id;

                var created = new Lease
                {
                    OrganizationId = organizationId,
                    RoomId = room.Id,
                    PreviousLeaseId = request.PreviousLeaseId,
                    LeaseCode = request.LeaseCode ?? GenerateLeaseCode(),
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    BillingDay = request.BillingDay,
                    MonthlyRent = monthlyRent,
                    DepositAmount = depositAmount,
                    CreatedBy = currentUserId,
                    Tenants = tenants
                        .Select(t => new LeaseTenant
                        {
                            UserId = t.UserId,
                            User = users[t.UserId],
                            IsRepresentative = t.IsRepresentative,
                            JoinedDate = t.JoinedDate,
                            CreatedBy = currentUserId
                        })
                        .ToList()
                };

                EnsureTenantsJoinWithinLeaseTerm(created);

                var propertyServices = await utilityServiceRepository.ListByPropertyIdAsync(
                    room.PropertyId, type: null, isActive: true, forUpdate: true, ct);

                created.AttachDefaultUtilityServices(propertyServices, currentUserId);

                await leaseRepository.AddAsync(created, ct);

                await leaseStatusHistoryRepository.AddAsync(new LeaseStatusHistory
                {
                    LeaseId = created.Id,
                    NewStatus = created.Status,
                    ChangedBy = currentUserId,
                    ChangedAt = DateTimeOffset.UtcNow
                }, ct);

                return created;
            }, cancellationToken);

            logger.LogInformation("Lease created ({LeaseId})", lease.Id);
            return lease.Adapt<LeaseResponse>();
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.LeaseOrganizationIdLeaseCode)
        {
            logger.LogWarning("Create lease failed: lease code already in use in organization (room {RoomId})", request.RoomId);
            throw new AppConflictException(request.LeaseCode is null
                ? "The generated lease code collided with an existing one, please retry"
                : "A lease with this code already exists in the organization");
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.LeasePreviousLeaseId)
        {
            logger.LogWarning("Create lease failed: previous lease already renewed ({PreviousLeaseId})", request.PreviousLeaseId);
            throw new AppConflictException("The previous lease has already been renewed by another lease");
        }
    }

    public async Task<LeaseResponse> UpdateAsync(Guid id, UpdateLeaseRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var lease = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var roomId = await leaseRepository.GetRoomIdAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

                _ = await roomRepository.GetByIdWithOrganizationIdForShareAsync(roomId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

                var existing = await leaseRepository.GetByIdWithRoomForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

                await organizationScopeAccessor.EnsureScopeAsync(
                    existing.OrganizationId,
                    PermissionResources.Lease,
                    PermissionActions.Update,
                    NotFoundMessages.Lease(id),
                    ct);

                if (existing.RoomId != roomId)
                {
                    throw new AppConflictException("The lease was modified concurrently, please retry");
                }

                switch (existing.Status)
                {
                    case LeaseStatus.Draft:
                        break;
                    case LeaseStatus.Active when HasContractChanges(request, existing):
                        throw new AppConflictException("Contract terms of an active lease cannot be changed");
                    case LeaseStatus.Active:
                        break;
                    default:
                        throw new AppConflictException("Only draft or active leases can be edited");
                }

                await leaseRepository.LoadTenantsWithUsersAsync(existing, ct);

                request.Adapt(existing, LeaseMappingConfig.UpdateConfig);

                if (existing.EndDate is { } endDate && endDate <= existing.StartDate)
                {
                    throw new AppValidationException("EndDate must be after StartDate");
                }

                if (request.PreviousLeaseId is { IsSet: true, Value: { } previousLeaseId })
                {
                    if (previousLeaseId == existing.Id)
                    {
                        throw new AppValidationException("A lease cannot be its own previous lease");
                    }

                    await EnsureCanBePreviousLeaseAsync(previousLeaseId, existing.OrganizationId, existing.RoomId, existing.Id, ct);

                    if (await leaseRepository.IsInRenewalChainAsync(existing.Id, previousLeaseId, ct))
                    {
                        throw new AppValidationException("PreviousLeaseId would create a cycle in the renewal chain");
                    }
                }

                if (request.Tenants.IsSet)
                {
                    await ApplyTenantChangesAsync(existing, request.Tenants.Value!, ct);
                }

                EnsureTenantsJoinWithinLeaseTerm(existing);

                leaseRepository.Update(existing);

                return existing;
            }, cancellationToken);

            logger.LogInformation("Lease updated ({LeaseId})", lease.Id);
            return lease.Adapt<LeaseResponse>();
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.LeaseOrganizationIdLeaseCode)
        {
            logger.LogWarning("Update lease failed: lease code already in use in organization ({LeaseId})", id);
            throw new AppConflictException("A lease with this code already exists in the organization");
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.LeasePreviousLeaseId)
        {
            logger.LogWarning("Update lease failed: previous lease already renewed ({LeaseId})", id);
            throw new AppConflictException("The previous lease has already been renewed by another lease");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var lease = await leaseRepository.GetByIdForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Delete,
                NotFoundMessages.Lease(id),
                ct);

            if (!lease.IsEditable)
            {
                throw new AppConflictException("Only draft leases can be deleted");
            }

            if (await leaseRepository.ExistsByPreviousLeaseIdAsync(id, cancellationToken: ct))
            {
                throw new AppConflictException("Cannot delete a lease that is the previous lease of another lease");
            }

            await leaseRepository.LoadTenantsAsync(lease, ct);

            leaseRepository.SoftDelete(lease);
        }, cancellationToken);

        logger.LogInformation("Lease soft-deleted ({LeaseId})", id);
    }

    public async Task<LeaseResponse> ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var lease = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var roomId = await leaseRepository.GetRoomIdAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

                var (room, _) = await roomRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

                var existing = await leaseRepository.GetByIdWithRoomForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

                await organizationScopeAccessor.EnsureScopeAsync(
                    existing.OrganizationId,
                    PermissionResources.Lease,
                    PermissionActions.Activate,
                    NotFoundMessages.Lease(id),
                    ct);

                if (existing.RoomId != room.Id)
                {
                    throw new AppConflictException("The lease was modified concurrently, please retry");
                }

                if (!existing.CanTransitionTo(LeaseStatus.Active))
                {
                    throw new AppConflictException("Only draft leases can be activated");
                }

                await leaseRepository.LoadTenantsWithUsersAsync(existing, ct);

                EnsureTenantsCanBeActivated(existing, room);

                if (existing.EndDate is { } endDate && endDate <= DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    throw new AppValidationException("Cannot activate a lease whose end date has passed");
                }

                if (room.RoomStatus == RoomStatus.Maintenance)
                {
                    throw new AppConflictException("Room is under maintenance");
                }

                if (await leaseRepository.ExistsActiveByRoomIdAsync(room.Id, ct))
                {
                    throw new AppConflictException("Room already has an active lease");
                }

                var history = existing.Activate(DateTimeOffset.UtcNow, currentUserAccessor.RequiredUser.Id);

                await leaseStatusHistoryRepository.AddAsync(history, ct);

                return existing;
            }, cancellationToken);

            logger.LogInformation("Lease activated ({LeaseId})", lease.Id);
            return lease.Adapt<LeaseResponse>();
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.LeaseRoomIdActive)
        {
            logger.LogWarning("Activate lease failed: room already has an active lease ({LeaseId})", id);
            throw new AppConflictException("Room already has an active lease");
        }
    }

    public async Task<LeaseResponse> TerminateAsync(Guid id, TerminateLeaseRequest request, CancellationToken cancellationToken = default)
    {
        var lease = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var roomId = await leaseRepository.GetRoomIdAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

            var (room, _) = await roomRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

            var existing = await leaseRepository.GetByIdWithRoomForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

            await organizationScopeAccessor.EnsureScopeAsync(
                existing.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Terminate,
                NotFoundMessages.Lease(id),
                ct);

            if (existing.RoomId != room.Id)
            {
                throw new AppConflictException("The lease was modified concurrently, please retry");
            }

            if (!existing.CanTransitionTo(LeaseStatus.Terminated))
            {
                throw new AppConflictException("Only active leases can be terminated");
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var latestLeftDate = today > existing.StartDate ? today : existing.StartDate;
            var leftDate = request.LeftDate ?? latestLeftDate;

            if (leftDate < existing.StartDate || leftDate > latestLeftDate)
            {
                throw new AppValidationException(
                    "LeftDate must be between the lease StartDate and today (or StartDate if the lease has not started yet)");
            }

            await leaseRepository.LoadTenantsWithUsersAsync(existing, ct);

            var joinedLater = existing.Tenants.FirstOrDefault(t =>
                (t.LeftDate is null || t.LeftDate > leftDate) && t.JoinedDate > leftDate);
            if (joinedLater is not null)
            {
                throw new AppValidationException(
                    $"LeftDate must not be before tenant JoinedDate {joinedLater.JoinedDate:yyyy-MM-dd}");
            }

            var history = existing.Terminate(DateTimeOffset.UtcNow, currentUserAccessor.RequiredUser.Id, leftDate, request.Reason);

            await leaseStatusHistoryRepository.AddAsync(history, ct);

            return existing;
        }, cancellationToken);

        logger.LogInformation("Lease terminated ({LeaseId})", lease.Id);
        return lease.Adapt<LeaseResponse>();
    }

    public async Task<List<LeaseStatusHistoryResponse>> GetStatusHistoriesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organizationId = await leaseRepository.GetOrganizationIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Lease,
            PermissionActions.Read,
            NotFoundMessages.Lease(id),
            cancellationToken);

        var histories = await leaseStatusHistoryRepository.ListByLeaseIdAsync(id, cancellationToken);

        return histories.Adapt<List<LeaseStatusHistoryResponse>>();
    }

    private static void EnsureTenantsCanBeActivated(Lease lease, Room room)
    {
        if (lease.Tenants.Count == 0)
        {
            throw new AppValidationException("A lease must have at least one tenant to be activated");
        }

        if (lease.Tenants.Count(t => t.IsRepresentative) != 1)
        {
            throw new AppValidationException("A lease must have exactly one representative tenant to be activated");
        }

        if (room.Capacity is { } capacity && lease.Tenants.Count > capacity)
        {
            throw new AppValidationException($"The room allows at most {capacity} tenants");
        }

        var unavailable = lease.Tenants.FirstOrDefault(t => t.User is not { IsActive: true, IsDeleted: false });

        if (unavailable is not null)
        {
            throw new AppValidationException($"User '{unavailable.UserId}' is inactive and cannot be a tenant of an active lease");
        }
    }

    private static bool HasContractChanges(UpdateLeaseRequest request, Lease lease) =>
        Changes(request.PreviousLeaseId, lease.PreviousLeaseId)
        || Changes(request.LeaseCode, lease.LeaseCode)
        || Changes(request.StartDate, lease.StartDate)
        || Changes(request.EndDate, lease.EndDate)
        || Changes(request.BillingDay, lease.BillingDay)
        || Changes(request.MonthlyRent, lease.MonthlyRent)
        || Changes(request.DepositAmount, lease.DepositAmount);

    private static bool Changes<T>(Optional<T> field, T? current) =>
        field.IsSet && !EqualityComparer<T?>.Default.Equals(field.Value, current);

    private async Task ApplyTenantChangesAsync(
        Lease lease, List<UpdateLeaseTenantRequest> changes, CancellationToken cancellationToken)
    {
        var existingById = lease.Tenants.ToDictionary(t => t.Id);
        var unknown = changes.FirstOrDefault(c => c.Id is not null && !existingById.ContainsKey(c.Id.Value));
        if (unknown is not null)
        {
            throw new AppNotFoundException(NotFoundMessages.LeaseTenant(unknown.Id!.Value));
        }

        var isActive = lease.Status == LeaseStatus.Active;

        if (isActive && changes.Any(c => c.IsDeleted))
        {
            throw new AppConflictException("Tenants of an active lease cannot be removed — set LeftDate instead");
        }

        if (!isActive && changes.Any(c => c.LeftDate.IsSet))
        {
            throw new AppValidationException("LeftDate can only be set on an active lease");
        }

        if (isActive && changes.Any(c => c.Id is not null && c.JoinedDate is { IsSet: true, Value: null }))
        {
            throw new AppValidationException("JoinedDate cannot be cleared on an active lease");
        }

        var updated = changes.Where(c => c is { Id: not null, IsDeleted: false }).ToDictionary(c => c.Id!.Value);
        var deletedIds = changes.Where(c => c.IsDeleted).Select(c => c.Id!.Value).ToHashSet();
        var added = changes.Where(c => c.Id is null).ToList();
        var remaining = lease.Tenants.Where(t => !deletedIds.Contains(t.Id)).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latestMoveDate = today > lease.StartDate ? today : lease.StartDate;
        var defaultJoinedDate = isActive ? latestMoveDate : (DateOnly?)null;

        var currentUserIds = remaining.Where(t => LeftDateAfter(t, updated) is null).Select(t => t.UserId)
            .Concat(added.Select(c => c.UserId!.Value))
            .ToList();
        var duplicateUserId = currentUserIds.GroupBy(u => u).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateUserId is not null)
        {
            throw new AppConflictException($"User '{duplicateUserId}' is already a tenant of this lease");
        }

        var overlappingStay = added.FirstOrDefault(c =>
            (c.JoinedDate.Value ?? defaultJoinedDate) is { } joined
            && remaining.Any(t => t.UserId == c.UserId && LeftDateAfter(t, updated) > joined));
        if (overlappingStay is not null)
        {
            throw new AppValidationException(
                $"User '{overlappingStay.UserId}' cannot rejoin before the LeftDate of their previous stay");
        }

        var currentCount = currentUserIds.Count;
        if (currentCount > Lease.MaxTenants)
        {
            throw new AppValidationException($"A lease must not have more than {Lease.MaxTenants} current tenants");
        }

        if (lease.Room!.Capacity is { } capacity && currentCount > capacity)
        {
            throw new AppValidationException($"The room allows at most {capacity} tenants");
        }

        if (remaining.Any(t => LeftDateAfter(t, updated) is not null && IsRepresentativeAfter(t, updated)))
        {
            throw new AppValidationException("A tenant who has moved out cannot be the representative");
        }

        var representativeCount = remaining.Count(t => IsRepresentativeAfter(t, updated))
            + added.Count(c => c.IsRepresentative == true);
        if (representativeCount > 1)
        {
            throw new AppValidationException("A lease can have at most one representative tenant");
        }

        if (isActive && currentCount == 0)
        {
            throw new AppValidationException("An active lease must keep at least one tenant who has not moved out");
        }

        if (isActive && representativeCount != 1)
        {
            throw new AppValidationException("An active lease must have exactly one representative tenant");
        }

        var invalidLeftDate = remaining.FirstOrDefault(t => LeftDateAfter(t, updated) is { } left
            && (left < (JoinedDateAfter(t, updated) ?? lease.StartDate) || left > latestMoveDate || left > lease.EndDate));
        if (invalidLeftDate is not null)
        {
            throw new AppValidationException(
                $"Tenant LeftDate {LeftDateAfter(invalidLeftDate, updated):yyyy-MM-dd} must be between their JoinedDate and today " +
                "(or StartDate if the lease has not started yet)");
        }

        var users = await EnsureUsersCanBeTenantsAsync(added.Select(c => c.UserId!.Value).ToList(), cancellationToken);

        foreach (var tenant in lease.Tenants.Where(t => deletedIds.Contains(t.Id)).ToList())
        {
            lease.Tenants.Remove(tenant);
        }

        foreach (var (tenantId, change) in updated)
        {
            var tenant = existingById[tenantId];
            change.JoinedDate.ApplyIfSet(v => tenant.JoinedDate = v);
            change.LeftDate.ApplyIfSet(v => tenant.LeftDate = v);
            if (change.IsRepresentative == false)
            {
                tenant.IsRepresentative = false;
            }
        }

        var promoted = updated.Where(u => u.Value.IsRepresentative == true && !existingById[u.Key].IsRepresentative).ToList();
        var freesSlots = deletedIds.Count > 0
            || updated.Any(u => u.Value.IsRepresentative == false || u.Value.LeftDate.IsSet);
        if (freesSlots && (added.Count > 0 || promoted.Count > 0))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        foreach (var (tenantId, _) in promoted)
        {
            existingById[tenantId].IsRepresentative = true;
        }

        var userId = currentUserAccessor.RequiredUser.Id;
        foreach (var change in added)
        {
            leaseRepository.AddTenant(lease, new LeaseTenant
            {
                LeaseId = lease.Id,
                UserId = change.UserId!.Value,
                User = users[change.UserId!.Value],
                IsRepresentative = change.IsRepresentative ?? false,
                JoinedDate = change.JoinedDate.Value ?? defaultJoinedDate,
                CreatedBy = userId
            });
        }
    }

    private static DateOnly? LeftDateAfter(LeaseTenant tenant, Dictionary<Guid, UpdateLeaseTenantRequest> updated) =>
        updated.TryGetValue(tenant.Id, out var c) && c.LeftDate.IsSet ? c.LeftDate.Value : tenant.LeftDate;

    private static DateOnly? JoinedDateAfter(LeaseTenant tenant, Dictionary<Guid, UpdateLeaseTenantRequest> updated) =>
        updated.TryGetValue(tenant.Id, out var c) && c.JoinedDate.IsSet ? c.JoinedDate.Value : tenant.JoinedDate;

    private static bool IsRepresentativeAfter(LeaseTenant tenant, Dictionary<Guid, UpdateLeaseTenantRequest> updated) =>
        updated.TryGetValue(tenant.Id, out var c) ? c.IsRepresentative ?? tenant.IsRepresentative : tenant.IsRepresentative;

    private async Task<Dictionary<Guid, User>> EnsureUsersCanBeTenantsAsync(List<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var users = await userRepository.ListByIdsAsync(userIds, cancellationToken);
        var foundIds = users.Select(u => u.Id).ToHashSet();
        var missing = userIds.FirstOrDefault(id => !foundIds.Contains(id));
        if (missing != Guid.Empty)
        {
            throw new AppNotFoundException(NotFoundMessages.User(missing));
        }

        var inactive = users.FirstOrDefault(u => !u.IsActive);
        if (inactive is not null)
        {
            throw new AppValidationException($"User '{inactive.Id}' is inactive and cannot be added to a lease");
        }

        return users.ToDictionary(u => u.Id);
    }

    private async Task EnsureCanBePreviousLeaseAsync(
        Guid previousLeaseId, Guid organizationId, Guid roomId, Guid? leaseId, CancellationToken cancellationToken)
    {
        var previous = await leaseRepository.GetByIdForShareAsync(previousLeaseId, cancellationToken);
        if (previous is null || previous.OrganizationId != organizationId)
        {
            throw new AppNotFoundException(NotFoundMessages.Lease(previousLeaseId));
        }

        if (previous.RoomId != roomId)
        {
            throw new AppValidationException("The previous lease must belong to the same room");
        }

        if (previous.Status == LeaseStatus.Draft)
        {
            throw new AppValidationException("Previous lease must not be a draft");
        }

        if (await leaseRepository.ExistsByPreviousLeaseIdAsync(previousLeaseId, leaseId, cancellationToken))
        {
            throw new AppConflictException("The previous lease has already been renewed by another lease");
        }
    }

    private static void EnsureTenantsJoinWithinLeaseTerm(Lease lease)
    {
        var outOfRange = lease.Tenants.FirstOrDefault(t =>
            t.JoinedDate is { } joined && (joined < lease.StartDate || joined >= lease.EndDate));
        if (outOfRange is not null)
        {
            throw new AppValidationException(
                $"Tenant JoinedDate {outOfRange.JoinedDate:yyyy-MM-dd} must be on or after StartDate and before EndDate");
        }
    }

    private static string GenerateLeaseCode() =>
        $"HD-{DateTime.UtcNow:yyMMddHHmmss}{RandomNumberGenerator.GetInt32(1_000_000):D6}";
}
