using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Lease : BaseEntity
{
    public const int MaxTenants = 20;
    public const string ExpiredReason = "End date reached";

    public Guid OrganizationId { get; private set; }
    public Organization? Organization { get; private set; }

    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }

    public Guid? PreviousLeaseId { get; private set; }
    public Lease? PreviousLease { get; private set; }

    public string LeaseCode { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }

    // Null = open-ended lease.
    public DateOnly? EndDate { get; private set; }

    // 1–28; null = property / organization default, then the day of StartDate (InvoiceCalculator.ResolveBillingDay).
    public int? BillingDay { get; private set; }

    public decimal MonthlyRent { get; private set; }
    public decimal DepositAmount { get; private set; }

    public LeaseStatus Status { get; private set; } = LeaseStatus.Draft;
    public DateTimeOffset? SignedAt { get; private set; }
    public DateTimeOffset? TerminatedAt { get; private set; }
    public string? TerminationReason { get; private set; }

    private readonly List<LeaseTenant> _tenants = [];
    private readonly List<LeaseUtilityService> _utilityServices = [];
    private readonly List<LeaseStatusHistory> _statusHistories = [];

    public IReadOnlyCollection<LeaseTenant> Tenants => _tenants;
    public IReadOnlyCollection<LeaseUtilityService> UtilityServices => _utilityServices;
    public IReadOnlyCollection<LeaseStatusHistory> StatusHistories => _statusHistories;

    private PendingTenantChanges? _pendingTenantChanges;

    public bool IsEditable => Status == LeaseStatus.Draft;

    public bool HasPendingTenantChanges => _pendingTenantChanges is not null;

    private Lease() { }

    public static Lease CreateDraft(
        Guid organizationId,
        Room room,
        Guid? previousLeaseId,
        string leaseCode,
        DateOnly startDate,
        DateOnly? endDate,
        int? billingDay,
        decimal? monthlyRent,
        decimal? depositAmount,
        IReadOnlyList<TenantChange> tenants,
        IReadOnlyDictionary<Guid, User> users,
        IReadOnlyList<LeaseUtilityServiceSelection> utilityServices,
        DateTimeOffset now,
        Guid createdBy)
    {
        var rent = monthlyRent ?? room.MonthlyRent
            ?? throw new DomainException("MonthlyRent is required because the room has no listed rent");

        var deposit = depositAmount ?? room.DepositAmount
            ?? throw new DomainException("DepositAmount is required because the room has no listed deposit");

        var terms = new LeaseTerms(previousLeaseId, leaseCode, startDate, endDate, billingDay, rent, deposit);
        EnsureTermsValid(terms);

        var lease = new Lease
        {
            OrganizationId = organizationId,
            RoomId = room.Id,
            Room = room,
            CreatedBy = createdBy
        };
        lease.ApplyTerms(terms);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        lease.EnsureTenantChangesValid(tenants, users, today, room.Capacity);
        lease.AddTenants(tenants, users, today, createdBy);

        foreach (var selection in utilityServices)
        {
            lease.AttachUtilityService(selection.Service, room.PropertyId, selection.UnitPriceOverride, selection.Quantity, createdBy);
        }

        lease._statusHistories.Add(new LeaseStatusHistory(lease.Id, oldStatus: null, lease.Status, reason: null, createdBy, now));

        return lease;
    }

    public bool CanTransitionTo(LeaseStatus next) =>
        AllowedTransitions[Status].Contains(next) && (next != LeaseStatus.Expired || EndDate is not null);

    public bool IsDueToExpire(DateOnly today) => CanTransitionTo(LeaseStatus.Expired) && EndDate < today;

    public void Update(
        LeaseTerms terms,
        IReadOnlyList<TenantChange>? tenantChanges,
        IReadOnlyDictionary<Guid, User> newUsers,
        DateOnly today,
        Guid actor)
    {
        switch (Status)
        {
            case LeaseStatus.Draft:
                break;
            case LeaseStatus.Active when terms != CurrentTerms:
                throw DomainException.Conflict("Contract terms of an active lease cannot be changed");
            case LeaseStatus.Active:
                break;
            default:
                throw DomainException.Conflict("Only draft or active leases can be edited");
        }

        EnsureTermsValid(terms);

        if (terms.PreviousLeaseId == Id)
        {
            throw new DomainException("A lease cannot be its own previous lease");
        }

        ApplyTerms(terms);

        if (tenantChanges is null)
        {
            EnsureTenantsJoinWithinTerm();
            return;
        }

        EnsureTenantChangesValid(tenantChanges, newUsers, today, RequiredRoom.Capacity);
        ApplyTenantRemovalsAndUpdates(tenantChanges);

        _pendingTenantChanges = tenantChanges.Any(c => c.TenantId is null || c is { IsDeleted: false, IsRepresentative: true })
            ? new PendingTenantChanges(tenantChanges, newUsers, today, actor)
            : null;
    }

    public void ApplyPendingTenantChanges()
    {
        var pending = _pendingTenantChanges
            ?? throw new InvalidOperationException("No pending tenant changes: call Update with tenant changes first");

        _pendingTenantChanges = null;
        AddTenants(pending.Changes, pending.NewUsers, pending.Today, pending.Actor);
    }

    private void AddTenants(
        IReadOnlyList<TenantChange> tenantChanges,
        IReadOnlyDictionary<Guid, User> newUsers,
        DateOnly today,
        Guid createdBy)
    {
        foreach (var change in tenantChanges.Where(c => c is { TenantId: not null, IsDeleted: false, IsRepresentative: true }))
        {
            _tenants.Single(t => t.Id == change.TenantId).SetRepresentative(true);
        }

        var defaultJoinedDate = DefaultJoinedDate(today);
        foreach (var change in tenantChanges.Where(c => c.TenantId is null))
        {
            _tenants.Add(new LeaseTenant(
                Id,
                newUsers[change.UserId!.Value],
                change.IsRepresentative ?? false,
                change.JoinedDate.Value ?? defaultJoinedDate,
                createdBy));
        }
    }

    private void EnsureTenantChangesValid(
        IReadOnlyList<TenantChange> changes, IReadOnlyDictionary<Guid, User> newUsers, DateOnly today, int? roomCapacity)
    {
        if (changes.Any(c => c is { TenantId: null, UserId: null }))
        {
            throw new DomainException("UserId is required to add a tenant");
        }

        if (changes.Any(c => c is { TenantId: null, IsDeleted: true }))
        {
            throw new DomainException("Only existing tenants can be removed");
        }

        var existingIds = _tenants.Select(t => t.Id).ToHashSet();
        var unknown = changes.FirstOrDefault(c => c.TenantId is { } id && !existingIds.Contains(id));
        if (unknown is not null)
        {
            throw DomainException.NotFound($"Lease tenant '{unknown.TenantId}' not found");
        }

        var isActive = Status == LeaseStatus.Active;

        if (isActive && changes.Any(c => c.IsDeleted))
        {
            throw DomainException.Conflict("Tenants of an active lease cannot be removed — set LeftDate instead");
        }

        if (!isActive && changes.Any(c => c.LeftDate.IsSet))
        {
            throw new DomainException("LeftDate can only be set on an active lease");
        }

        if (isActive && changes.Any(c => c is { TenantId: not null, JoinedDate: { IsSet: true, Value: null } }))
        {
            throw new DomainException("JoinedDate cannot be cleared on an active lease");
        }

        var updated = changes.Where(c => c is { TenantId: not null, IsDeleted: false }).ToDictionary(c => c.TenantId!.Value);
        var deletedIds = changes.Where(c => c.IsDeleted).Select(c => c.TenantId!.Value).ToHashSet();
        var added = changes.Where(c => c.TenantId is null).ToList();
        var remaining = _tenants.Where(t => !deletedIds.Contains(t.Id)).ToList();
        var latestMoveDate = LatestMoveDate(today);
        var defaultJoinedDate = DefaultJoinedDate(today);

        DateOnly? LeftDateAfter(LeaseTenant t) =>
            updated.TryGetValue(t.Id, out var c) ? c.LeftDate.OrKeep(t.LeftDate) : t.LeftDate;

        DateOnly? JoinedDateAfter(LeaseTenant t) =>
            updated.TryGetValue(t.Id, out var c) ? c.JoinedDate.OrKeep(t.JoinedDate) : t.JoinedDate;

        bool IsRepresentativeAfter(LeaseTenant t) =>
            updated.TryGetValue(t.Id, out var c) ? c.IsRepresentative ?? t.IsRepresentative : t.IsRepresentative;

        var currentUserIds = remaining.Where(t => LeftDateAfter(t) is null).Select(t => t.UserId)
            .Concat(added.Select(c => c.UserId!.Value))
            .ToList();
        var duplicateUserId = currentUserIds.GroupBy(u => u).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateUserId is not null)
        {
            throw DomainException.Conflict($"User '{duplicateUserId}' is already a tenant of this lease");
        }

        var overlappingStay = added.FirstOrDefault(c =>
            (c.JoinedDate.Value ?? defaultJoinedDate) is { } joined
            && remaining.Any(t => t.UserId == c.UserId && LeftDateAfter(t) > joined));
        if (overlappingStay is not null)
        {
            throw new DomainException(
                $"User '{overlappingStay.UserId}' cannot rejoin before the LeftDate of their previous stay");
        }

        var currentCount = currentUserIds.Count;
        if (currentCount > MaxTenants)
        {
            throw new DomainException($"A lease must not have more than {MaxTenants} current tenants");
        }

        if (roomCapacity is { } capacity && currentCount > capacity)
        {
            throw new DomainException($"The room allows at most {capacity} tenants");
        }

        if (remaining.Any(t => LeftDateAfter(t) is not null && IsRepresentativeAfter(t)))
        {
            throw new DomainException("A tenant who has moved out cannot be the representative");
        }

        var representativeCount = remaining.Count(IsRepresentativeAfter) + added.Count(c => c.IsRepresentative == true);
        if (representativeCount > 1)
        {
            throw new DomainException("A lease can have at most one representative tenant");
        }

        if (isActive && currentCount == 0)
        {
            throw new DomainException("An active lease must keep at least one tenant who has not moved out");
        }

        if (isActive && representativeCount != 1)
        {
            throw new DomainException("An active lease must have exactly one representative tenant");
        }

        var invalidLeftDate = remaining.FirstOrDefault(t => LeftDateAfter(t) is { } left
            && (left < (JoinedDateAfter(t) ?? StartDate) || left > latestMoveDate || left > EndDate));
        if (invalidLeftDate is not null)
        {
            throw new DomainException(
                $"Tenant LeftDate {LeftDateAfter(invalidLeftDate):yyyy-MM-dd} must be between their JoinedDate and today " +
                "(or StartDate if the lease has not started yet)");
        }

        var inactive = added.Select(c => newUsers[c.UserId!.Value]).FirstOrDefault(u => !u.IsActive);
        if (inactive is not null)
        {
            throw new DomainException($"User '{inactive.Id}' is inactive and cannot be added to a lease");
        }

        EnsureJoinedWithinTerm(remaining.Select(JoinedDateAfter)
            .Concat(added.Select(c => c.JoinedDate.Value ?? defaultJoinedDate)));
    }

    private void ApplyTenantRemovalsAndUpdates(IReadOnlyList<TenantChange> changes)
    {
        // Removing orphans the tenant, so EF marks it Deleted and the audit interceptor soft-deletes it.
        var deletedIds = changes.Where(c => c.IsDeleted).Select(c => c.TenantId!.Value).ToHashSet();
        _tenants.RemoveAll(t => deletedIds.Contains(t.Id));

        foreach (var change in changes.Where(c => c is { TenantId: not null, IsDeleted: false }))
        {
            var tenant = _tenants.Single(t => t.Id == change.TenantId);
            if (change.JoinedDate.IsSet)
            {
                tenant.SetJoinedDate(change.JoinedDate.Value);
            }

            if (change.LeftDate.IsSet)
            {
                tenant.SetLeftDate(change.LeftDate.Value);
            }

            if (change.IsRepresentative == false)
            {
                tenant.SetRepresentative(false);
            }
        }
    }

    private DateOnly LatestMoveDate(DateOnly today) => today > StartDate ? today : StartDate;

    private DateOnly? DefaultJoinedDate(DateOnly today) =>
        Status == LeaseStatus.Active ? LatestMoveDate(today) : null;

    public LeaseTerms CurrentTerms =>
        new(PreviousLeaseId, LeaseCode, StartDate, EndDate, BillingDay, MonthlyRent, DepositAmount);

    public void EnsureCanBeRenewedFor(Guid roomId)
    {
        if (RoomId != roomId)
        {
            throw new DomainException("The previous lease must belong to the same room");
        }

        if (Status == LeaseStatus.Draft)
        {
            throw new DomainException("Previous lease must not be a draft");
        }
    }

    public void EnsureCanBeDeleted()
    {
        if (!IsEditable)
        {
            throw DomainException.Conflict("Only draft leases can be deleted");
        }
    }

    public void Activate(DateTimeOffset now, DateOnly today, Guid changedBy)
    {
        EnsureCanTransitionTo(LeaseStatus.Active, "Only draft leases can be activated");

        if (Tenants.Count == 0)
        {
            throw new DomainException("A lease must have at least one tenant to be activated");
        }

        if (Tenants.Count(t => t.IsRepresentative) != 1)
        {
            throw new DomainException("A lease must have exactly one representative tenant to be activated");
        }

        if (RequiredRoom.Capacity is { } capacity && Tenants.Count > capacity)
        {
            throw new DomainException($"The room allows at most {capacity} tenants");
        }

        var unavailable = Tenants.FirstOrDefault(t => t.User is not { IsActive: true, IsDeleted: false });
        if (unavailable is not null)
        {
            throw new DomainException($"User '{unavailable.UserId}' is inactive and cannot be a tenant of an active lease");
        }

        if (EndDate is { } endDate && endDate <= today)
        {
            throw new DomainException("Cannot activate a lease whose end date has passed");
        }

        RequiredRoom.MarkOccupied();

        TransitionTo(LeaseStatus.Active, now, changedBy, reason: null);

        SignedAt = now;

        foreach (var tenant in Tenants.Where(t => t.JoinedDate is null))
        {
            tenant.SetJoinedDate(StartDate);
        }
    }

    public void Terminate(DateTimeOffset now, DateOnly today, Guid changedBy, DateOnly? leftDate, string reason)
    {
        EnsureCanTransitionTo(LeaseStatus.Terminated, "Only active leases can be terminated");

        var latestLeftDate = LatestMoveDate(today);
        var left = leftDate ?? latestLeftDate;

        if (left < StartDate || left > latestLeftDate)
        {
            throw new DomainException(
                "LeftDate must be between the lease StartDate and today (or StartDate if the lease has not started yet)");
        }

        var joinedLater = Tenants.FirstOrDefault(t => (t.LeftDate is null || t.LeftDate > left) && t.JoinedDate > left);
        if (joinedLater is not null)
        {
            throw new DomainException($"LeftDate must not be before tenant JoinedDate {joinedLater.JoinedDate:yyyy-MM-dd}");
        }

        TransitionTo(LeaseStatus.Terminated, now, changedBy, reason);
        TerminatedAt = now;
        TerminationReason = reason;
        End(left);
    }

    public void Expire(DateTimeOffset now, DateOnly today)
    {
        EnsureCanTransitionTo(LeaseStatus.Expired, $"Cannot transition lease from {Status} to {LeaseStatus.Expired}");

        if (!IsDueToExpire(today))
        {
            throw DomainException.Conflict($"Lease cannot expire before its end date {EndDate:yyyy-MM-dd} has passed");
        }

        TransitionTo(LeaseStatus.Expired, now, changedBy: null, ExpiredReason);
        End(EndDate!.Value);
    }

    public LeaseUtilityService UpsertUtilityService(UtilityService service, LeaseUtilityServiceChange change, Guid actor)
    {
        if (Status is not (LeaseStatus.Draft or LeaseStatus.Active))
        {
            throw DomainException.Conflict("Utility services can only be changed on draft or active leases");
        }

        var leaseService = UtilityServices.FirstOrDefault(s => s.ServiceId == service.Id)
            ?? AttachUtilityService(service, RequiredRoom.PropertyId, unitPriceOverride: null, quantity: 1, actor);

        if (change.UnitPriceOverride.IsSet)
        {
            leaseService.SetUnitPriceOverride(change.UnitPriceOverride.Value);
        }

        if (change.Quantity is { } quantity)
        {
            leaseService.SetQuantity(quantity);
        }

        if (change.IsActive is { } isActive)
        {
            leaseService.SetIsActive(isActive);
        }

        return leaseService;
    }

    public void RemoveUtilityService(Guid serviceId)
    {
        if (!IsEditable)
        {
            throw DomainException.Conflict(
                "Utility services can only be removed from draft leases — deactivate the service instead");
        }

        var leaseService = UtilityServices.FirstOrDefault(s => s.ServiceId == serviceId)
            ?? throw DomainException.NotFound($"Utility service '{serviceId}' is not attached to the lease");

        _utilityServices.Remove(leaseService);
    }

    private LeaseUtilityService AttachUtilityService(
        UtilityService service, Guid roomPropertyId, decimal? unitPriceOverride, int quantity, Guid createdBy)
    {
        if (service.PropertyId != roomPropertyId)
        {
            throw new DomainException($"Utility service '{service.Id}' must belong to the property of the lease's room");
        }

        if (!service.IsActive)
        {
            throw new DomainException(
                $"Utility service '{service.Name}' is inactive and cannot be attached to a lease");
        }

        if (_utilityServices.Any(s => s.ServiceId == service.Id))
        {
            throw DomainException.Conflict($"Utility service '{service.Name}' is already attached to the lease");
        }

        var attached = new LeaseUtilityService(Id, service, unitPriceOverride, quantity, createdBy);

        _utilityServices.Add(attached);

        return attached;
    }

    private static void EnsureTermsValid(LeaseTerms terms)
    {
        if (terms.EndDate is { } endDate && endDate <= terms.StartDate)
        {
            throw new DomainException("EndDate must be after StartDate");
        }

        BillingDayRule.EnsureValid(terms.BillingDay, nameof(BillingDay));

        if (terms.MonthlyRent <= 0)
        {
            throw new DomainException("MonthlyRent must be greater than 0");
        }

        if (terms.DepositAmount < 0)
        {
            throw new DomainException("DepositAmount must not be negative");
        }
    }

    private void ApplyTerms(LeaseTerms terms)
    {
        PreviousLeaseId = terms.PreviousLeaseId;
        LeaseCode = terms.LeaseCode;
        StartDate = terms.StartDate;
        EndDate = terms.EndDate;
        BillingDay = terms.BillingDay;
        MonthlyRent = terms.MonthlyRent;
        DepositAmount = terms.DepositAmount;
    }

    private void EnsureTenantsJoinWithinTerm() => EnsureJoinedWithinTerm(Tenants.Select(t => t.JoinedDate));

    private void EnsureJoinedWithinTerm(IEnumerable<DateOnly?> joinedDates)
    {
        var outOfRange = joinedDates.FirstOrDefault(d => d is { } joined && (joined < StartDate || joined >= EndDate));
        if (outOfRange is not null)
        {
            throw new DomainException(
                $"Tenant JoinedDate {outOfRange:yyyy-MM-dd} must be on or after StartDate and before EndDate");
        }
    }

    private Room RequiredRoom =>
        Room ?? throw new InvalidOperationException("Lease.Room must be loaded");

    private static readonly Dictionary<LeaseStatus, LeaseStatus[]> AllowedTransitions = new()
    {
        [LeaseStatus.Draft] = [LeaseStatus.Active],
        [LeaseStatus.Active] = [LeaseStatus.Expired, LeaseStatus.Terminated],
        [LeaseStatus.Expired] = [],
        [LeaseStatus.Terminated] = []
    };

    private void EnsureCanTransitionTo(LeaseStatus next, string message)
    {
        if (!CanTransitionTo(next))
        {
            throw DomainException.Conflict(message);
        }
    }

    private void TransitionTo(LeaseStatus next, DateTimeOffset now, Guid? changedBy, string? reason)
    {
        _statusHistories.Add(new LeaseStatusHistory(Id, Status, next, reason, changedBy, now));
        Status = next;
    }

    private void End(DateOnly leftDate)
    {
        MoveOutTenants(leftDate);
        RequiredRoom.MarkAvailable();
    }

    private void MoveOutTenants(DateOnly leftDate)
    {
        foreach (var tenant in Tenants.Where(t => t.LeftDate is null || t.LeftDate > leftDate))
        {
            tenant.SetLeftDate(leftDate);
        }
    }

    private sealed record PendingTenantChanges(
        IReadOnlyList<TenantChange> Changes, IReadOnlyDictionary<Guid, User> NewUsers, DateOnly Today, Guid Actor);
}
