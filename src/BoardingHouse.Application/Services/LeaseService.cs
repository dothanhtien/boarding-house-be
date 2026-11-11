using System.Linq.Expressions;
using System.Security.Cryptography;
using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public class LeaseService(
    IUserRepository userRepository,
    IRoomRepository roomRepository,
    ILeaseRepository leaseRepository,
    IUtilityServiceRepository utilityServiceRepository,
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

                if (request.PreviousLeaseId is { } previousLeaseId)
                {
                    await EnsureCanBePreviousLeaseAsync(previousLeaseId, organizationId, room.Id, leaseId: null, ct);
                }

                var tenants = request.Tenants ?? [];
                var users = await GetUsersAsync(tenants.Select(t => t.UserId).ToList(), ct);
                var utilityServices = await GetUtilityServiceSelectionsAsync(request.UtilityServices ?? [], ct);
                var currentUserId = currentUserAccessor.RequiredUser.Id;

                var created = Lease.CreateDraft(
                    organizationId,
                    room,
                    request.PreviousLeaseId,
                    request.LeaseCode ?? GenerateLeaseCode(),
                    request.StartDate,
                    request.EndDate,
                    request.BillingDay,
                    request.MonthlyRent,
                    request.DepositAmount,
                    tenants.Select(t => new TenantChange
                    {
                        UserId = t.UserId,
                        IsRepresentative = t.IsRepresentative,
                        JoinedDate = t.JoinedDate
                    }).ToList(),
                    users,
                    utilityServices,
                    DateTimeOffset.UtcNow,
                    currentUserId);

                await leaseRepository.AddAsync(created, ct);

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

                var existing = await leaseRepository.GetAggregateForUpdateAsync(id, ct)
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

                var current = existing.CurrentTerms;
                var terms = new LeaseTerms(
                    request.PreviousLeaseId.OrKeep(current.PreviousLeaseId),
                    request.LeaseCode.OrKeep(current.LeaseCode)!,
                    request.StartDate.OrKeep(current.StartDate),
                    request.EndDate.OrKeep(current.EndDate),
                    request.BillingDay.OrKeep(current.BillingDay),
                    request.MonthlyRent.OrKeep(current.MonthlyRent),
                    request.DepositAmount.OrKeep(current.DepositAmount));

                var tenantChanges = request.Tenants.IsSet ? request.Tenants.Value!.Select(ToTenantChange).ToList() : null;
                var newUsers = await GetUsersAsync(
                    tenantChanges?.Where(c => c.TenantId is null).Select(c => c.UserId!.Value).ToList() ?? [], ct);

                existing.Update(terms, tenantChanges, newUsers, Today, currentUserAccessor.RequiredUser.Id);

                if (request.PreviousLeaseId is { IsSet: true, Value: { } previousLeaseId })
                {
                    await EnsureCanBePreviousLeaseAsync(previousLeaseId, existing.OrganizationId, existing.RoomId, existing.Id, ct);

                    if (await leaseRepository.IsInRenewalChainAsync(existing.Id, previousLeaseId, ct))
                    {
                        throw new AppValidationException("PreviousLeaseId would create a cycle in the renewal chain");
                    }
                }

                if (existing.HasPendingTenantChanges)
                {
                    await unitOfWork.SaveChangesAsync(ct);
                    existing.ApplyPendingTenantChanges();
                }

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
            var lease = await leaseRepository.GetByIdWithTenantsForDeleteAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(id));

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Delete,
                NotFoundMessages.Lease(id),
                ct);

            lease.EnsureCanBeDeleted();

            if (await leaseRepository.ExistsByPreviousLeaseIdAsync(id, cancellationToken: ct))
            {
                throw new AppConflictException("Cannot delete a lease that is the previous lease of another lease");
            }

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

                var existing = await leaseRepository.GetAggregateForUpdateAsync(id, ct)
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

                if (existing.CanTransitionTo(LeaseStatus.Active) && await leaseRepository.ExistsActiveByRoomIdAsync(room.Id, ct))
                {
                    throw new AppConflictException("Room already has an active lease");
                }

                existing.Activate(DateTimeOffset.UtcNow, Today, currentUserAccessor.RequiredUser.Id);

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

            var existing = await leaseRepository.GetAggregateForUpdateAsync(id, ct)
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

            existing.Terminate(
                DateTimeOffset.UtcNow, Today, currentUserAccessor.RequiredUser.Id, request.LeftDate, request.Reason);

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

        var histories = await leaseRepository.ListStatusHistoriesAsync(id, cancellationToken);

        return histories.Adapt<List<LeaseStatusHistoryResponse>>();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static TenantChange ToTenantChange(UpdateLeaseTenantRequest request) => new()
    {
        TenantId = request.Id,
        UserId = request.UserId,
        IsRepresentative = request.IsRepresentative,
        JoinedDate = request.JoinedDate.ToChange(),
        LeftDate = request.LeftDate.ToChange(),
        IsDeleted = request.IsDeleted
    };

    private async Task<Dictionary<Guid, User>> GetUsersAsync(List<Guid> userIds, CancellationToken cancellationToken)
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

        return users.ToDictionary(u => u.Id);
    }

    private async Task<List<LeaseUtilityServiceSelection>> GetUtilityServiceSelectionsAsync(
        List<CreateLeaseUtilityServiceRequest> requested, CancellationToken cancellationToken)
    {
        if (requested.Count == 0)
        {
            return [];
        }

        var services = (await utilityServiceRepository.ListByIdsForShareAsync(
                requested.Select(s => s.ServiceId).ToList(), cancellationToken))
            .ToDictionary(s => s.Id);

        return requested
            .Select(item => services.TryGetValue(item.ServiceId, out var service)
                ? new LeaseUtilityServiceSelection(service, item.UnitPriceOverride, item.Quantity)
                : throw new AppValidationException(
                    $"Utility service '{item.ServiceId}' must belong to the property of the lease's room"))
            .ToList();
    }

    private async Task EnsureCanBePreviousLeaseAsync(
        Guid previousLeaseId, Guid organizationId, Guid roomId, Guid? leaseId, CancellationToken cancellationToken)
    {
        var previous = await leaseRepository.GetByIdForShareAsync(previousLeaseId, cancellationToken);
        if (previous is null || previous.OrganizationId != organizationId)
        {
            throw new AppNotFoundException(NotFoundMessages.Lease(previousLeaseId));
        }

        previous.EnsureCanBeRenewedFor(roomId);

        if (await leaseRepository.ExistsByPreviousLeaseIdAsync(previousLeaseId, leaseId, cancellationToken))
        {
            throw new AppConflictException("The previous lease has already been renewed by another lease");
        }
    }

    private static string GenerateLeaseCode() =>
        $"HD-{DateTime.UtcNow:yyMMddHHmmss}{RandomNumberGenerator.GetInt32(1_000_000):D6}";
}
