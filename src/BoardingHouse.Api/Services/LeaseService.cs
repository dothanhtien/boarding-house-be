using System.Linq.Expressions;
using System.Security.Cryptography;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.Leases;
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

public class LeaseService(
    IRoomRepository roomRepository,
    ILeaseRepository leaseRepository,
    IUserRepository userRepository,
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
            throw new AppNotFoundException($"Organization '{organizationId}' not found");
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
            ?? throw new AppNotFoundException($"Lease '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            lease.OrganizationId,
            PermissionResources.Lease,
            PermissionActions.Read,
            $"Lease '{id}' not found",
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
                    ?? throw new AppNotFoundException($"Room '{request.RoomId}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    organizationId,
                    PermissionResources.Lease,
                    PermissionActions.Create,
                    $"Room '{request.RoomId}' not found",
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

                await leaseRepository.AddAsync(created, ct);
                return created;
            }, cancellationToken);

            logger.LogInformation("Lease created ({LeaseId})", lease.Id);

            return lease.Adapt<LeaseResponse>();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(LeaseConfiguration.OrganizationIdLeaseCodeUniqueIndex))
        {
            logger.LogWarning("Create lease failed: lease code already in use in organization (room {RoomId})", request.RoomId);
            throw new AppConflictException(request.LeaseCode is null
                ? "The generated lease code collided with an existing one, please retry"
                : "A lease with this code already exists in the organization");
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(LeaseConfiguration.PreviousLeaseIdUniqueIndex))
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
                var existing = await leaseRepository.GetByIdWithRoomForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException($"Lease '{id}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    existing.OrganizationId,
                    PermissionResources.Lease,
                    PermissionActions.Update,
                    $"Lease '{id}' not found",
                    ct);

                if (!existing.IsEditable)
                {
                    throw new AppConflictException("Only draft leases can be edited");
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
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(LeaseConfiguration.OrganizationIdLeaseCodeUniqueIndex))
        {
            logger.LogWarning("Update lease failed: lease code already in use in organization ({LeaseId})", id);
            throw new AppConflictException("A lease with this code already exists in the organization");
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(LeaseConfiguration.PreviousLeaseIdUniqueIndex))
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
                ?? throw new AppNotFoundException($"Lease '{id}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Delete,
                $"Lease '{id}' not found",
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

    private async Task ApplyTenantChangesAsync(Lease lease, List<UpdateLeaseTenantRequest> changes, CancellationToken cancellationToken)
    {
        var existingById = lease.Tenants.ToDictionary(t => t.Id);
        var unknown = changes.FirstOrDefault(c => c.Id is not null && !existingById.ContainsKey(c.Id.Value));
        if (unknown is not null)
        {
            throw new AppNotFoundException($"Lease tenant '{unknown.Id}' not found");
        }

        var updated = changes.Where(c => c is { Id: not null, IsDeleted: false }).ToDictionary(c => c.Id!.Value);
        var deletedIds = changes.Where(c => c.IsDeleted).Select(c => c.Id!.Value).ToHashSet();
        var added = changes.Where(c => c.Id is null).ToList();
        var remaining = lease.Tenants.Where(t => !deletedIds.Contains(t.Id)).ToList();

        var duplicate = added.FirstOrDefault(c => remaining.Any(t => t.UserId == c.UserId));
        if (duplicate is not null)
        {
            throw new AppConflictException($"User '{duplicate.UserId}' is already a tenant of this lease");
        }

        var tenantCount = remaining.Count + added.Count;
        if (tenantCount > Lease.MaxTenants)
        {
            throw new AppValidationException($"A lease must not have more than {Lease.MaxTenants} tenants");
        }

        if (lease.Room!.Capacity is { } capacity && tenantCount > capacity)
        {
            throw new AppValidationException($"The room allows at most {capacity} tenants");
        }

        var representativeCount =
            remaining.Count(t => updated.TryGetValue(t.Id, out var c) ? c.IsRepresentative ?? t.IsRepresentative : t.IsRepresentative)
            + added.Count(c => c.IsRepresentative == true);
        if (representativeCount > 1)
        {
            throw new AppValidationException("A lease can have at most one representative tenant");
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
            if (change.IsRepresentative == false)
            {
                tenant.IsRepresentative = false;
            }
        }

        var promoted = updated.Where(u => u.Value.IsRepresentative == true && !existingById[u.Key].IsRepresentative).ToList();
        var freesSlots = deletedIds.Count > 0 || updated.Any(u => u.Value.IsRepresentative == false);
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
                JoinedDate = change.JoinedDate.Value,
                CreatedBy = userId
            });
        }
    }

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
            throw new AppNotFoundException($"User '{missing}' not found");
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
            throw new AppNotFoundException($"Lease '{previousLeaseId}' not found");
        }

        if (previous.RoomId != roomId)
        {
            throw new AppValidationException("The previous lease must belong to the same room");
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
