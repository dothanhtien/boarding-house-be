using BoardingHouse.UnitTests.Common;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class LeaseTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2027, 1, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid PropertyId = Guid.NewGuid();

    private sealed record TenantSpec(User User, bool IsRepresentative, DateOnly? JoinedDate, DateOnly? LeftDate);

    private static Lease NewLease(LeaseStatus status, params TenantSpec[] tenants)
    {
        if (status == LeaseStatus.Draft && tenants.Any(t => t.LeftDate is not null))
        {
            throw new ArgumentException("Only an active lease can have tenants who moved out", nameof(tenants));
        }

        if (status != LeaseStatus.Draft && !tenants.Any(t => t.IsRepresentative))
        {
            tenants = [.. tenants, NewTenant(isRepresentative: true)];
        }

        var lease = NewDraft(tenants);

        if (status == LeaseStatus.Draft)
        {
            return lease;
        }

        lease.Activate(Now, StartDate, SentinelActors.System);

        var moveOuts = tenants
            .Where(t => t.LeftDate is not null)
            .Select(t => new TenantChange { TenantId = Of(lease, t).Id, LeftDate = t.LeftDate })
            .ToList();
        if (moveOuts.Count > 0)
        {
            UpdateTenants(lease, moveOuts);
        }

        switch (status)
        {
            case LeaseStatus.Terminated:
                lease.Terminate(Now, Today, SentinelActors.System, leftDate: null, "reason");
                break;
            case LeaseStatus.Expired:
                lease.Expire(Now, EndDate.AddDays(1));
                break;
        }

        return lease;
    }

    private static Lease NewDraft(TenantSpec[] tenants, params LeaseUtilityServiceSelection[] utilityServices) =>
        Lease.CreateDraft(
            Guid.NewGuid(), TestRooms.New(propertyId: PropertyId), previousLeaseId: null, "HD-TEST", StartDate, EndDate,
            billingDay: null, monthlyRent: 3_000_000, depositAmount: 3_000_000,
            tenants.Select(t => NewTenantChange(t.User, t.IsRepresentative, t.JoinedDate)).ToList(),
            UsersOf(tenants.Select(t => t.User).ToArray()),
            utilityServices, Now, SentinelActors.System);

    private static User NewUser(bool isActive = true) => TestEntities.New<User>(new
    {
        FullName = "Tenant",
        Phone = "0900000000",
        PasswordHash = "hash",
        IsActive = isActive,
        CreatedBy = SentinelActors.System
    });

    private static TenantSpec NewTenant(
        DateOnly? joinedDate = null, DateOnly? leftDate = null, bool isRepresentative = false, User? user = null) =>
        new(user ?? NewUser(), isRepresentative, joinedDate, leftDate);

    private static LeaseTenant Of(Lease lease, TenantSpec tenant) => lease.Tenants.Single(t => t.UserId == tenant.User.Id);

    private static UtilityService NewService(
        decimal defaultUnitPrice,
        bool isActive = true,
        UtilityBillingMethod billingMethod = UtilityBillingMethod.Fixed,
        Guid? propertyId = null) => TestEntities.New<UtilityService>(new
    {
        PropertyId = propertyId ?? PropertyId,
        Name = "Service",
        Type = UtilityType.Other,
        BillingMethod = billingMethod,
        Unit = "month",
        DefaultUnitPrice = defaultUnitPrice,
        IsActive = isActive,
        CreatedBy = SentinelActors.System
    });

    private static readonly IReadOnlyDictionary<Guid, User> NoUsers = new Dictionary<Guid, User>();

    private static TenantChange NewTenantChange(User user, bool isRepresentative = false, DateOnly? joinedDate = null) =>
        new() { UserId = user.Id, IsRepresentative = isRepresentative, JoinedDate = joinedDate };

    private static IReadOnlyDictionary<Guid, User> UsersOf(params User[] users) => users.ToDictionary(u => u.Id);

    // Both steps of a tenant update, as LeaseService runs them (minus the save in between).
    private static void UpdateTenants(Lease lease, IReadOnlyList<TenantChange> changes, params User[] newUsers) =>
        UpdateTenants(lease, lease.CurrentTerms, changes, newUsers);

    private static void UpdateTenants(
        Lease lease, LeaseTerms terms, IReadOnlyList<TenantChange> changes, params User[] newUsers)
    {
        var users = newUsers.ToDictionary(u => u.Id);
        lease.Update(terms, changes, users, Today, ActorId);
        if (lease.HasPendingTenantChanges)
        {
            lease.ApplyPendingTenantChanges();
        }
    }

    public static TheoryData<LeaseStatus, LeaseStatus, bool> AllTransitions()
    {
        var allowed = new HashSet<(LeaseStatus, LeaseStatus)>
        {
            (LeaseStatus.Draft, LeaseStatus.Active),
            (LeaseStatus.Active, LeaseStatus.Expired),
            (LeaseStatus.Active, LeaseStatus.Terminated)
        };

        var data = new TheoryData<LeaseStatus, LeaseStatus, bool>();
        foreach (var from in Enum.GetValues<LeaseStatus>())
        {
            foreach (var to in Enum.GetValues<LeaseStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void CanTransitionTo_OnlyAllowsDeclaredEdges(LeaseStatus from, LeaseStatus to, bool expected)
    {
        Assert.Equal(expected, NewLease(from).CanTransitionTo(to));
    }

    [Fact]
    public void CanTransitionTo_OpenEndedActiveLease_CannotExpire()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant(isRepresentative: true));
        lease.Update(lease.CurrentTerms with { EndDate = null }, tenantChanges: null, NoUsers, Today, ActorId);
        lease.Activate(Now, Today, ActorId);

        Assert.False(lease.CanTransitionTo(LeaseStatus.Expired));
    }

    // ---- CreateDraft ----

    [Fact]
    public void CreateDraft_FallsBackToRoomPricesAndRecordsHistory()
    {
        var room = TestRooms.New(monthlyRent: 2_500_000, depositAmount: 5_000_000);
        var user = NewUser();

        var lease = Lease.CreateDraft(
            Guid.NewGuid(), room, previousLeaseId: null, "HD-1", StartDate, EndDate, billingDay: 5,
            monthlyRent: null, depositAmount: null, [NewTenantChange(user, isRepresentative: true)], UsersOf(user),
            utilityServices: [], Now, ActorId);
        var history = Assert.Single(lease.StatusHistories);

        Assert.Equal(LeaseStatus.Draft, lease.Status);
        Assert.Equal((2_500_000m, 5_000_000m), (lease.MonthlyRent, lease.DepositAmount));
        var tenant = Assert.Single(lease.Tenants);
        Assert.Equal((user.Id, true, ActorId), (tenant.UserId, tenant.IsRepresentative, tenant.CreatedBy));
        Assert.Equal((lease.Id, (LeaseStatus?)null, LeaseStatus.Draft, (Guid?)ActorId, Now),
            (history.LeaseId, history.OldStatus, history.NewStatus, history.ChangedBy, history.ChangedAt));
    }

    [Fact]
    public void CreateDraft_NoRentAnywhere_Throws()
    {
        var room = TestRooms.New();

        var ex = Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), room, null, "HD-1", StartDate, EndDate, null, monthlyRent: null, depositAmount: 1, [], NoUsers, [], Now, ActorId));

        Assert.Equal("MonthlyRent is required because the room has no listed rent", ex.Message);
    }

    [Fact]
    public void CreateDraft_MoreTenantsThanCapacity_Throws()
    {
        var room = TestRooms.New(capacity: 1);
        var (first, second) = (NewUser(), NewUser());

        Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), room, null, "HD-1", StartDate, EndDate, null, 1, 1,
            [NewTenantChange(first, isRepresentative: true), NewTenantChange(second)], UsersOf(first, second), [], Now, ActorId));
    }

    [Fact]
    public void CreateDraft_InactiveUser_Throws()
    {
        var room = TestRooms.New();

        var inactive = NewUser(isActive: false);

        Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), room, null, "HD-1", StartDate, EndDate, null, 1, 1,
            [NewTenantChange(inactive, isRepresentative: true)], UsersOf(inactive), [], Now, ActorId));
    }

    [Fact]
    public void CreateDraft_JoinedDateOutsideTerm_Throws()
    {
        var room = TestRooms.New();

        var user = NewUser();

        Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), room, null, "HD-1", StartDate, EndDate, null, 1, 1,
            [NewTenantChange(user, isRepresentative: true, joinedDate: EndDate)], UsersOf(user), [], Now, ActorId));
    }

    // ---- Update: terms ----

    [Fact]
    public void Update_DraftLease_ChangesTerms()
    {
        var lease = NewLease(LeaseStatus.Draft);
        var terms = lease.CurrentTerms with { MonthlyRent = 4_000_000, EndDate = new DateOnly(2027, 6, 1) };

        lease.Update(terms, tenantChanges: null, NoUsers, Today, ActorId);

        Assert.Equal(terms, lease.CurrentTerms);
    }

    [Fact]
    public void Update_ActiveLeaseWithChangedTerms_ThrowsConflict()
    {
        var lease = NewLease(LeaseStatus.Active);

        var ex = Assert.Throws<DomainException>(() =>
            lease.Update(lease.CurrentTerms with { MonthlyRent = 1 }, null, NoUsers, Today, ActorId));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Update_ActiveLeaseWithSameTerms_IsAllowed()
    {
        var lease = NewLease(LeaseStatus.Active);

        lease.Update(lease.CurrentTerms, null, NoUsers, Today, ActorId);
    }

    [Theory]
    [InlineData(LeaseStatus.Expired)]
    [InlineData(LeaseStatus.Terminated)]
    public void Update_ClosedLease_ThrowsConflict(LeaseStatus status)
    {
        var lease = NewLease(status);

        var ex = Assert.Throws<DomainException>(() => lease.Update(lease.CurrentTerms, null, NoUsers, Today, ActorId));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Update_EndDateNotAfterStartDate_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft);

        Assert.Throws<DomainException>(() =>
            lease.Update(lease.CurrentTerms with { EndDate = StartDate }, null, NoUsers, Today, ActorId));
    }

    [Fact]
    public void Update_SelfAsPreviousLease_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft);

        Assert.Throws<DomainException>(() =>
            lease.Update(lease.CurrentTerms with { PreviousLeaseId = lease.Id }, null, NoUsers, Today, ActorId));
    }

    [Fact]
    public void Update_NewStartDateAndTenantJoinedDateTogether_ChecksTheFinalState()
    {
        var tenant = NewTenant(joinedDate: new DateOnly(2026, 1, 5));
        var lease = NewLease(LeaseStatus.Draft, tenant);
        var newStart = new DateOnly(2026, 2, 1);

        UpdateTenants(
            lease,
            lease.CurrentTerms with { StartDate = newStart },
            [new TenantChange { TenantId = Of(lease, tenant).Id, JoinedDate = newStart }]);

        Assert.Equal(newStart, Of(lease, tenant).JoinedDate);
    }

    [Fact]
    public void Update_NewStartDateLeavesTenantJoinedBefore_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant(joinedDate: new DateOnly(2026, 1, 5)));

        Assert.Throws<DomainException>(() =>
            lease.Update(lease.CurrentTerms with { StartDate = new DateOnly(2026, 2, 1) }, null, NoUsers, Today, ActorId));
    }

    [Fact]
    public void UpdateTenants_SwapRepresentativeInOneBatch_IsAllowed()
    {
        var oldRepresentative = NewTenant(joinedDate: StartDate, isRepresentative: true);
        var newRepresentative = NewTenant(joinedDate: StartDate);
        var lease = NewLease(LeaseStatus.Active, oldRepresentative, newRepresentative);

        UpdateTenants(lease,
        [
            new TenantChange { TenantId = Of(lease, oldRepresentative).Id, IsRepresentative = false },
            new TenantChange { TenantId = Of(lease, newRepresentative).Id, IsRepresentative = true }
        ]);

        Assert.False(Of(lease, oldRepresentative).IsRepresentative);
        Assert.True(Of(lease, newRepresentative).IsRepresentative);
    }

    [Fact]
    public void UpdateTenants_MoveOutAndAddReplacement_AddsNewTenantJoiningToday()
    {
        var room = TestRooms.New(capacity: 1);
        var leaving = NewTenant(joinedDate: StartDate, isRepresentative: true);
        var lease = TestEntities.Set(NewLease(LeaseStatus.Active, leaving), new { Room = room });
        var newcomer = NewUser();

        UpdateTenants(lease,
        [
            new TenantChange { TenantId = Of(lease, leaving).Id, IsRepresentative = false, LeftDate = Today },
            new TenantChange { UserId = newcomer.Id, IsRepresentative = true }
        ], newcomer);

        var tenant = Assert.Single(lease.Tenants, t => t.UserId == newcomer.Id);
        Assert.Equal((newcomer.Id, Today, true, ActorId), (tenant.UserId, tenant.JoinedDate, tenant.IsRepresentative, tenant.CreatedBy));
        Assert.Equal(Today, Of(lease, leaving).LeftDate);
    }

    [Fact]
    public void UpdateTenants_TwoRepresentatives_Throws()
    {
        var representative = NewTenant(isRepresentative: true);
        var other = NewTenant();
        var lease = NewLease(LeaseStatus.Draft, representative, other);

        Assert.Throws<DomainException>(() =>
            UpdateTenants(lease, [new TenantChange { TenantId = Of(lease, other).Id, IsRepresentative = true }]));
    }

    [Fact]
    public void UpdateTenants_MoreThanCapacity_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant());
        lease.Room!.Update(lease.Room.Details with { Capacity = 1 });
        var user = NewUser();

        Assert.Throws<DomainException>(() => UpdateTenants(lease, [new TenantChange { UserId = user.Id }], user));
    }

    [Fact]
    public void UpdateTenants_RemoveFromActiveLease_ThrowsConflict()
    {
        var tenant = NewTenant(joinedDate: StartDate, isRepresentative: true);
        var lease = NewLease(LeaseStatus.Active, tenant);

        var ex = Assert.Throws<DomainException>(() =>
            UpdateTenants(lease, [new TenantChange { TenantId = Of(lease, tenant).Id, IsDeleted = true }]));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void UpdateTenants_LeftDateInTheFuture_Throws()
    {
        var tenant = NewTenant(joinedDate: StartDate);
        var representative = NewTenant(joinedDate: StartDate, isRepresentative: true);
        var lease = NewLease(LeaseStatus.Active, tenant, representative);

        Assert.Throws<DomainException>(() => UpdateTenants(lease,
            [new TenantChange { TenantId = Of(lease, tenant).Id, LeftDate = Today.AddDays(1) }]));
    }

    [Fact]
    public void UpdateTenants_UnknownTenant_ThrowsNotFound()
    {
        var lease = NewLease(LeaseStatus.Draft);

        var ex = Assert.Throws<DomainException>(() =>
            UpdateTenants(lease, [new TenantChange { TenantId = Guid.NewGuid(), IsRepresentative = true }]));

        Assert.Equal(DomainErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void UpdateTenants_InactiveNewUser_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft);
        var inactive = NewUser(isActive: false);

        Assert.Throws<DomainException>(() => UpdateTenants(lease, [new TenantChange { UserId = inactive.Id }], inactive));
    }

    [Fact]
    public void UpdateTenants_RemoveFromDraft_RemovesFromCollection()
    {
        var tenant = NewTenant();
        var lease = NewLease(LeaseStatus.Draft, tenant);

        UpdateTenants(lease, [new TenantChange { TenantId = Of(lease, tenant).Id, IsDeleted = true }]);

        Assert.Empty(lease.Tenants);
    }

    [Fact]
    public void Activate_FromDraft_SetsStatusJoinedDatesAndOccupiesRoom()
    {
        var joinedLater = new DateOnly(2026, 2, 1);
        var defaulted = NewTenant(isRepresentative: true);
        var explicitJoin = NewTenant(joinedDate: joinedLater);
        var lease = NewLease(LeaseStatus.Draft, defaulted, explicitJoin);

        lease.Activate(Now, Today, ActorId);
        var history = lease.StatusHistories.Last();

        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(Now, lease.SignedAt);
        Assert.Equal(StartDate, Of(lease, defaulted).JoinedDate);
        Assert.Equal(joinedLater, Of(lease, explicitJoin).JoinedDate);
        Assert.Equal(RoomStatus.Occupied, lease.Room!.RoomStatus);
        Assert.Equal(((LeaseStatus?)LeaseStatus.Draft, LeaseStatus.Active, (Guid?)ActorId, Now),
            (history.OldStatus, history.NewStatus, history.ChangedBy, history.ChangedAt));
    }

    [Theory]
    [InlineData(LeaseStatus.Active)]
    [InlineData(LeaseStatus.Expired)]
    [InlineData(LeaseStatus.Terminated)]
    public void Activate_FromNonDraft_ThrowsConflict(LeaseStatus status)
    {
        var ex = Assert.Throws<DomainException>(() => NewLease(status).Activate(Now, Today, ActorId));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Activate_WithoutTenants_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => NewLease(LeaseStatus.Draft).Activate(Now, Today, ActorId));

        Assert.Equal("A lease must have at least one tenant to be activated", ex.Message);
    }

    [Fact]
    public void Activate_WithoutRepresentative_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant());

        Assert.Throws<DomainException>(() => lease.Activate(Now, Today, ActorId));
    }

    [Fact]
    public void Activate_WithInactiveTenant_Throws()
    {
        var user = NewUser();
        var lease = NewLease(LeaseStatus.Draft, NewTenant(isRepresentative: true, user: user));
        user.SetActive(false);

        Assert.Throws<DomainException>(() => lease.Activate(Now, Today, ActorId));
    }

    [Fact]
    public void Activate_EndDatePassed_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant(isRepresentative: true));

        Assert.Throws<DomainException>(() => lease.Activate(Now, EndDate, ActorId));
    }

    [Fact]
    public void Activate_RoomUnderMaintenance_ThrowsConflict()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant(isRepresentative: true));
        lease.Room!.ChangeStatus(RoomStatus.Maintenance, hasActiveLease: false);

        var ex = Assert.Throws<DomainException>(() => lease.Activate(Now, Today, ActorId));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Transition_WithoutLoadedRoom_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant(isRepresentative: true));
        TestEntities.Set(lease, new { Room = (Room?)null });

        Assert.Throws<InvalidOperationException>(() => lease.Activate(Now, Today, ActorId));
    }

    [Fact]
    public void Terminate_SetsFieldsFreesRoomAndMovesOutCurrentTenants()
    {
        var movedOutEarlier = new DateOnly(2026, 5, 1);
        var current = NewTenant();
        var alreadyLeft = NewTenant(leftDate: movedOutEarlier);
        var lease = NewLease(LeaseStatus.Active, current, alreadyLeft);
        var leftDate = new DateOnly(2026, 10, 15);

        lease.Terminate(Now, Today, ActorId, leftDate, "Moved to another city");
        var history = lease.StatusHistories.Last();

        Assert.Equal(LeaseStatus.Terminated, lease.Status);
        Assert.Equal(Now, lease.TerminatedAt);
        Assert.Equal("Moved to another city", lease.TerminationReason);
        Assert.Equal(leftDate, Of(lease, current).LeftDate);
        Assert.Equal(movedOutEarlier, Of(lease, alreadyLeft).LeftDate);
        Assert.Equal(RoomStatus.Available, lease.Room!.RoomStatus);
        Assert.Equal(((LeaseStatus?)LeaseStatus.Active, LeaseStatus.Terminated, (string?)"Moved to another city"),
            (history.OldStatus, history.NewStatus, history.Reason));
    }

    [Fact]
    public void Terminate_WithoutLeftDate_UsesToday()
    {
        var tenant = NewTenant();
        var lease = NewLease(LeaseStatus.Active, tenant);

        lease.Terminate(Now, Today, ActorId, leftDate: null, "reason");

        Assert.Equal(Today, Of(lease, tenant).LeftDate);
    }

    [Fact]
    public void Terminate_ClampsMoveOutRecordedAfterLeftDate()
    {
        var leftAfter = NewTenant(leftDate: new DateOnly(2026, 10, 15));
        var lease = NewLease(LeaseStatus.Active, leftAfter);
        var leftDate = new DateOnly(2026, 10, 10);

        lease.Terminate(Now, Today, ActorId, leftDate, "reason");

        Assert.Equal(leftDate, Of(lease, leftAfter).LeftDate);
    }

    [Fact]
    public void Terminate_LeftDateInTheFuture_Throws()
    {
        var lease = NewLease(LeaseStatus.Active, NewTenant());

        Assert.Throws<DomainException>(() => lease.Terminate(Now, Today, ActorId, Today.AddDays(1), "reason"));
    }

    [Fact]
    public void Terminate_LeftDateBeforeATenantJoined_Throws()
    {
        var lease = NewLease(LeaseStatus.Active, NewTenant(joinedDate: new DateOnly(2026, 6, 1)));

        Assert.Throws<DomainException>(() => lease.Terminate(Now, Today, ActorId, new DateOnly(2026, 5, 1), "reason"));
    }

    [Fact]
    public void Terminate_FromDraft_ThrowsConflict()
    {
        var ex = Assert.Throws<DomainException>(
            () => NewLease(LeaseStatus.Draft).Terminate(Now, Today, ActorId, StartDate, "reason"));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Expire_MovesOutCurrentTenantsOnEndDateFreesRoomAndRecordsHistory()
    {
        var tenant = NewTenant();
        var lease = NewLease(LeaseStatus.Active, tenant);

        lease.Expire(Now, EndDate.AddDays(1));
        var history = lease.StatusHistories.Last();

        Assert.Equal(LeaseStatus.Expired, lease.Status);
        Assert.Equal(EndDate, Of(lease, tenant).LeftDate);
        Assert.Equal(RoomStatus.Available, lease.Room!.RoomStatus);
        Assert.Equal(((LeaseStatus?)LeaseStatus.Active, LeaseStatus.Expired), (history.OldStatus, history.NewStatus));
        Assert.Null(history.ChangedBy);
        Assert.Equal(Lease.ExpiredReason, history.Reason);
    }

    [Fact]
    public void EnsureCanBeRenewedFor_OtherRoom_Throws()
    {
        var previous = NewLease(LeaseStatus.Expired);

        Assert.Throws<DomainException>(() => previous.EnsureCanBeRenewedFor(Guid.NewGuid()));
    }

    [Fact]
    public void EnsureCanBeRenewedFor_Draft_Throws()
    {
        var previous = NewLease(LeaseStatus.Draft);

        Assert.Throws<DomainException>(() => previous.EnsureCanBeRenewedFor(previous.RoomId));
    }

    [Fact]
    public void EnsureCanBeDeleted_NonDraft_ThrowsConflict()
    {
        var ex = Assert.Throws<DomainException>(() => NewLease(LeaseStatus.Active).EnsureCanBeDeleted());

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void CreateDraft_WithUtilityServices_AttachesWithOverrideAndQuantity()
    {
        var airConditioner = NewService(defaultUnitPrice: 150_000);

        var lease = NewDraft([], new LeaseUtilityServiceSelection(airConditioner, UnitPriceOverride: 120_000, Quantity: 2));

        var attached = Assert.Single(lease.UtilityServices);
        Assert.Equal(lease.Id, attached.LeaseId);
        Assert.Equal(airConditioner.Id, attached.ServiceId);
        Assert.Equal(120_000, attached.UnitPriceOverride);
        Assert.Equal(120_000, attached.EffectiveUnitPrice);
        Assert.Equal(2, attached.Quantity);
        Assert.True(attached.IsActive);
        Assert.Equal(SentinelActors.System, attached.CreatedBy);
    }

    [Fact]
    public void CreateDraft_InactiveService_Throws()
    {
        var inactive = NewService(defaultUnitPrice: 50_000, isActive: false);

        Assert.Throws<DomainException>(() => NewDraft([], new LeaseUtilityServiceSelection(inactive, null, 1)));
    }

    [Fact]
    public void CreateDraft_ServiceOfAnotherProperty_Throws()
    {
        var service = NewService(defaultUnitPrice: 100_000, propertyId: Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() => NewDraft([], new LeaseUtilityServiceSelection(service, null, 1)));

        Assert.Equal($"Utility service '{service.Id}' must belong to the property of the lease's room", ex.Message);
    }

    [Fact]
    public void CreateDraft_SameServiceTwice_ThrowsConflict()
    {
        var service = NewService(defaultUnitPrice: 100_000);

        var ex = Assert.Throws<DomainException>(() => NewDraft(
            [], new LeaseUtilityServiceSelection(service, null, 1), new LeaseUtilityServiceSelection(service, null, 1)));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Theory]
    [InlineData(UtilityBillingMethod.Metered)]
    [InlineData(UtilityBillingMethod.PerTenant)]
    public void CreateDraft_QuantityOnNonFixedService_Throws(UtilityBillingMethod billingMethod)
    {
        var service = NewService(defaultUnitPrice: 15_000, billingMethod: billingMethod);

        Assert.Throws<DomainException>(() => NewDraft([], new LeaseUtilityServiceSelection(service, null, 2)));
    }

    [Theory]
    [InlineData(UtilityBillingMethod.Metered)]
    [InlineData(UtilityBillingMethod.PerTenant)]
    public void CreateDraft_DefaultQuantityOnNonFixedService_Attaches(UtilityBillingMethod billingMethod)
    {
        var service = NewService(defaultUnitPrice: 15_000, billingMethod: billingMethod);

        var lease = NewDraft([], new LeaseUtilityServiceSelection(service, null, 1));

        Assert.Equal(1, Assert.Single(lease.UtilityServices).Quantity);
    }

    [Fact]
    public void UpsertUtilityService_New_AttachesThenAppliesChange()
    {
        var lease = NewLease(LeaseStatus.Active);
        var service = NewService(defaultUnitPrice: 100_000);

        var attached = lease.UpsertUtilityService(
            service, new LeaseUtilityServiceChange { UnitPriceOverride = 80_000, Quantity = 3 }, ActorId);

        Assert.Same(attached, Assert.Single(lease.UtilityServices));
        Assert.Equal((80_000m, 3, true), (attached.EffectiveUnitPrice, attached.Quantity, attached.IsActive));
    }

    [Fact]
    public void UpsertUtilityService_Existing_ChangesOnlyTheSetFields()
    {
        var service = NewService(defaultUnitPrice: 100_000);
        var lease = NewDraft([], new LeaseUtilityServiceSelection(service, UnitPriceOverride: 90_000, Quantity: 2));
        var attached = Assert.Single(lease.UtilityServices);

        lease.UpsertUtilityService(service, new LeaseUtilityServiceChange { IsActive = false }, ActorId);

        Assert.Single(lease.UtilityServices);
        Assert.Equal((90_000m, 2, false), (attached.EffectiveUnitPrice, attached.Quantity, attached.IsActive));
    }

    [Fact]
    public void UpsertUtilityService_UnitPriceOverrideSetToNull_FallsBackToDefaultPrice()
    {
        var service = NewService(defaultUnitPrice: 100_000);
        var lease = NewDraft([], new LeaseUtilityServiceSelection(service, UnitPriceOverride: 90_000, Quantity: 1));
        var attached = Assert.Single(lease.UtilityServices);

        lease.UpsertUtilityService(service, new LeaseUtilityServiceChange { UnitPriceOverride = null }, ActorId);

        Assert.Equal(((decimal?)null, 100_000m), (attached.UnitPriceOverride, attached.EffectiveUnitPrice));
    }

    [Fact]
    public void UpsertUtilityService_NegativeUnitPriceOverride_ThrowsAndKeepsPrice()
    {
        var service = NewService(defaultUnitPrice: 100_000);
        var lease = NewDraft([], new LeaseUtilityServiceSelection(service, UnitPriceOverride: 90_000, Quantity: 1));
        var attached = Assert.Single(lease.UtilityServices);

        var ex = Assert.Throws<DomainException>(() =>
            lease.UpsertUtilityService(service, new LeaseUtilityServiceChange { UnitPriceOverride = -1 }, ActorId));

        Assert.Equal(("UnitPriceOverride must not be negative", DomainErrorKind.Validation), (ex.Message, ex.Kind));
        Assert.Equal(90_000m, attached.EffectiveUnitPrice);
    }

    [Fact]
    public void UpsertUtilityService_ServiceOfAnotherProperty_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft);
        var service = NewService(defaultUnitPrice: 100_000, propertyId: Guid.NewGuid());

        Assert.Throws<DomainException>(() => lease.UpsertUtilityService(service, new LeaseUtilityServiceChange(), ActorId));
    }

    [Theory]
    [InlineData(LeaseStatus.Expired)]
    [InlineData(LeaseStatus.Terminated)]
    public void UpsertUtilityService_ClosedLease_ThrowsConflict(LeaseStatus status)
    {
        var lease = NewLease(status);
        var service = NewService(defaultUnitPrice: 100_000);

        var ex = Assert.Throws<DomainException>(() =>
            lease.UpsertUtilityService(service, new LeaseUtilityServiceChange(), ActorId));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void RemoveUtilityService_Draft_Removes()
    {
        var service = NewService(defaultUnitPrice: 100_000);
        var lease = NewDraft([], new LeaseUtilityServiceSelection(service, null, 1));

        lease.RemoveUtilityService(service.Id);

        Assert.Empty(lease.UtilityServices);
    }

    // ---- TICKET-047 ----

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void CreateDraft_BillingDayOutOfRange_Throws(int billingDay)
    {
        var ex = Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), TestRooms.New(), null, "HD-1", StartDate, EndDate, billingDay, 1, 1, [], NoUsers, [], Now, ActorId));

        Assert.Equal("BillingDay must be between 1 and 28", ex.Message);
    }

    [Fact]
    public void CreateDraft_EndDateNotAfterStartDate_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), TestRooms.New(), null, "HD-1", StartDate, StartDate, null, 1, 1, [], NoUsers, [], Now, ActorId));

        Assert.Equal("EndDate must be after StartDate", ex.Message);
    }

    [Theory]
    [InlineData(0, 1, "MonthlyRent must be greater than 0")]
    [InlineData(1, -1, "DepositAmount must not be negative")]
    public void CreateDraft_InvalidAmounts_Throws(decimal monthlyRent, decimal depositAmount, string message)
    {
        var ex = Assert.Throws<DomainException>(() => Lease.CreateDraft(
            Guid.NewGuid(), TestRooms.New(), null, "HD-1", StartDate, EndDate, null, monthlyRent, depositAmount,
            [], NoUsers, [], Now, ActorId));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Expire_BeforeEndDatePassed_ThrowsConflictAndKeepsLeaseActive()
    {
        var lease = NewLease(LeaseStatus.Active);

        var ex = Assert.Throws<DomainException>(() => lease.Expire(Now, EndDate));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal(LeaseStatus.Active, lease.Status);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void IsDueToExpire_OnlyAfterEndDate(int todayOffsetFromEndDate, bool expected)
    {
        var lease = NewLease(LeaseStatus.Active);

        Assert.Equal(expected, lease.IsDueToExpire(EndDate.AddDays(-todayOffsetFromEndDate)));
    }

    [Fact]
    public void Update_TenantChangesWithoutAdditionOrPromotion_HasNoPendingChanges()
    {
        var tenant = NewTenant();
        var lease = NewLease(LeaseStatus.Draft, tenant);

        lease.Update(lease.CurrentTerms, [new TenantChange { TenantId = Of(lease, tenant).Id, IsDeleted = true }], NoUsers, Today, ActorId);

        Assert.False(lease.HasPendingTenantChanges);
    }

    [Fact]
    public void Update_TenantAddition_IsAppliedOnlyByApplyPendingTenantChanges()
    {
        var lease = NewLease(LeaseStatus.Draft);
        var user = NewUser();

        lease.Update(lease.CurrentTerms, [new TenantChange { UserId = user.Id }], UsersOf(user), Today, ActorId);

        Assert.True(lease.HasPendingTenantChanges);
        Assert.Empty(lease.Tenants);

        lease.ApplyPendingTenantChanges();

        Assert.False(lease.HasPendingTenantChanges);
        Assert.Equal((user.Id, ActorId), (Assert.Single(lease.Tenants).UserId, lease.Tenants.Single().CreatedBy));
    }

    [Fact]
    public void ApplyPendingTenantChanges_WithoutUpdate_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => NewLease(LeaseStatus.Draft).ApplyPendingTenantChanges());
    }

    [Fact]
    public void Activate_RecordsHistoryOnTheLease()
    {
        var lease = NewLease(LeaseStatus.Draft, NewTenant(isRepresentative: true));

        lease.Activate(Now, Today, ActorId);

        Assert.Equal([LeaseStatus.Draft, LeaseStatus.Active], lease.StatusHistories.Select(h => h.NewStatus));
    }

    [Fact]
    public void RemoveUtilityService_ActiveLease_ThrowsConflict()
    {
        var lease = NewLease(LeaseStatus.Active);

        var ex = Assert.Throws<DomainException>(() => lease.RemoveUtilityService(Guid.NewGuid()));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void RemoveUtilityService_NotAttached_ThrowsNotFound()
    {
        var lease = NewLease(LeaseStatus.Draft);

        var ex = Assert.Throws<DomainException>(() => lease.RemoveUtilityService(Guid.NewGuid()));

        Assert.Equal(DomainErrorKind.NotFound, ex.Kind);
    }
}
