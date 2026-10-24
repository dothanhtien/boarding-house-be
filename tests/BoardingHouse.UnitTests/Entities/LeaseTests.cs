using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class LeaseTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2027, 1, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId = Guid.NewGuid();

    private static Lease NewLease(LeaseStatus status, params LeaseTenant[] tenants) => new()
    {
        LeaseCode = "HD-TEST",
        CreatedBy = SentinelActors.System,
        StartDate = StartDate,
        EndDate = EndDate,
        Status = status,
        Room = new Room { RoomNumber = "101", CreatedBy = SentinelActors.System },
        Tenants = tenants.ToList()
    };

    private static LeaseTenant NewTenant(DateOnly? joinedDate = null, DateOnly? leftDate = null) => new()
    {
        CreatedBy = SentinelActors.System,
        JoinedDate = joinedDate,
        LeftDate = leftDate
    };

    private static UtilityService NewService(decimal? defaultUnitPrice, bool isActive = true) => new()
    {
        Name = "Service",
        Type = UtilityType.Other,
        BillingMethod = UtilityBillingMethod.Fixed,
        Unit = "month",
        DefaultUnitPrice = defaultUnitPrice,
        IsActive = isActive,
        CreatedBy = SentinelActors.System
    };

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
        var lease = NewLease(LeaseStatus.Active);
        lease.EndDate = null;

        Assert.False(lease.CanTransitionTo(LeaseStatus.Expired));
    }

    [Fact]
    public void Activate_FromDraft_SetsStatusJoinedDatesAndOccupiesRoom()
    {
        var joinedLater = new DateOnly(2026, 2, 1);
        var defaulted = NewTenant();
        var explicitJoin = NewTenant(joinedDate: joinedLater);
        var lease = NewLease(LeaseStatus.Draft, defaulted, explicitJoin);

        var history = lease.Activate(Now, ActorId);

        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(Now, lease.SignedAt);
        Assert.Equal(StartDate, defaulted.JoinedDate);
        Assert.Equal(joinedLater, explicitJoin.JoinedDate);
        Assert.Equal(RoomStatus.Occupied, lease.Room!.RoomStatus);
        Assert.Equal(((LeaseStatus?)LeaseStatus.Draft, LeaseStatus.Active, (Guid?)ActorId, Now),
            (history.OldStatus, history.NewStatus, history.ChangedBy, history.ChangedAt));
    }

    [Theory]
    [InlineData(LeaseStatus.Active)]
    [InlineData(LeaseStatus.Expired)]
    [InlineData(LeaseStatus.Terminated)]
    public void Activate_FromNonDraft_Throws(LeaseStatus status)
    {
        Assert.Throws<InvalidOperationException>(() => NewLease(status).Activate(Now, ActorId));
    }

    [Fact]
    public void Transition_WithoutLoadedRoom_Throws()
    {
        var lease = NewLease(LeaseStatus.Draft);
        lease.Room = null;

        Assert.Throws<InvalidOperationException>(() => lease.Activate(Now, ActorId));
    }

    [Fact]
    public void Terminate_SetsFieldsFreesRoomAndMovesOutCurrentTenants()
    {
        var movedOutEarlier = new DateOnly(2026, 5, 1);
        var current = NewTenant();
        var alreadyLeft = NewTenant(leftDate: movedOutEarlier);
        var lease = NewLease(LeaseStatus.Active, current, alreadyLeft);
        lease.Room!.RoomStatus = RoomStatus.Occupied;
        var leftDate = new DateOnly(2026, 10, 15);

        var history = lease.Terminate(Now, ActorId, leftDate, "Moved to another city");

        Assert.Equal(LeaseStatus.Terminated, lease.Status);
        Assert.Equal(Now, lease.TerminatedAt);
        Assert.Equal("Moved to another city", lease.TerminationReason);
        Assert.Equal(leftDate, current.LeftDate);
        Assert.Equal(movedOutEarlier, alreadyLeft.LeftDate);
        Assert.Equal(RoomStatus.Available, lease.Room.RoomStatus);
        Assert.Equal(((LeaseStatus?)LeaseStatus.Active, LeaseStatus.Terminated, (string?)"Moved to another city"),
            (history.OldStatus, history.NewStatus, history.Reason));
    }

    [Fact]
    public void Terminate_ClampsMoveOutRecordedAfterLeftDate()
    {
        var leftAfter = NewTenant(leftDate: new DateOnly(2026, 10, 15));
        var lease = NewLease(LeaseStatus.Active, leftAfter);
        var leftDate = new DateOnly(2026, 10, 10);

        lease.Terminate(Now, ActorId, leftDate, "reason");

        Assert.Equal(leftDate, leftAfter.LeftDate);
    }

    [Fact]
    public void Terminate_FromDraft_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => NewLease(LeaseStatus.Draft).Terminate(Now, ActorId, StartDate, "reason"));
    }

    [Fact]
    public void Expire_MovesOutCurrentTenantsOnEndDateFreesRoomAndReturnsHistory()
    {
        var tenant = NewTenant();
        var lease = NewLease(LeaseStatus.Active, tenant);
        lease.Room!.RoomStatus = RoomStatus.Occupied;

        var history = lease.Expire(Now);

        Assert.Equal(LeaseStatus.Expired, lease.Status);
        Assert.Equal(EndDate, tenant.LeftDate);
        Assert.Equal(RoomStatus.Available, lease.Room.RoomStatus);
        Assert.Equal(((LeaseStatus?)LeaseStatus.Active, LeaseStatus.Expired), (history.OldStatus, history.NewStatus));
        Assert.Null(history.ChangedBy);
        Assert.Equal(Lease.ExpiredReason, history.Reason);
    }

    [Fact]
    public void AttachDefaultUtilityServices_AttachesOnlyActiveServicesWithDefaultPrice()
    {
        var lease = NewLease(LeaseStatus.Draft);
        var priced = NewService(defaultUnitPrice: 100_000);
        var unpriced = NewService(defaultUnitPrice: null);
        var inactive = NewService(defaultUnitPrice: 50_000, isActive: false);

        lease.AttachDefaultUtilityServices([priced, unpriced, inactive], ActorId);

        var attached = Assert.Single(lease.UtilityServices);
        Assert.Equal(lease.Id, attached.LeaseId);
        Assert.Equal(priced.Id, attached.ServiceId);
        Assert.Null(attached.UnitPriceOverride);
        Assert.True(attached.IsActive);
        Assert.Equal(ActorId, attached.CreatedBy);
    }
}
