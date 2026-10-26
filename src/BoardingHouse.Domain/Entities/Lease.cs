using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Lease : BaseEntity
{
    public const int MaxTenants = 20;
    public const string ExpiredReason = "End date reached";

    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid? PreviousLeaseId { get; set; }
    public Lease? PreviousLease { get; set; }

    public required string LeaseCode { get; set; }
    public DateOnly StartDate { get; set; }

    // Null = open-ended lease.
    public DateOnly? EndDate { get; set; }

    // 1–28; null = use the day of StartDate.
    public int? BillingDay { get; set; }

    public decimal MonthlyRent { get; set; }
    public decimal DepositAmount { get; set; }

    public LeaseStatus Status { get; set; } = LeaseStatus.Draft;
    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public string? TerminationReason { get; set; }

    public ICollection<LeaseTenant> Tenants { get; set; } = [];
    public ICollection<LeaseUtilityService> UtilityServices { get; set; } = [];

    public bool IsEditable => Status == LeaseStatus.Draft;

    public bool CanTransitionTo(LeaseStatus next) =>
        AllowedTransitions[Status].Contains(next) && (next != LeaseStatus.Expired || EndDate is not null);

    public LeaseStatusHistory Activate(DateTimeOffset now, Guid changedBy)
    {
        var history = TransitionTo(LeaseStatus.Active, now, changedBy, reason: null);

        SignedAt = now;

        foreach (var tenant in Tenants.Where(t => t.JoinedDate is null))
        {
            tenant.JoinedDate = StartDate;
        }

        RequiredRoom.RoomStatus = RoomStatus.Occupied;

        return history;
    }

    public LeaseStatusHistory Terminate(DateTimeOffset now, Guid changedBy, DateOnly leftDate, string reason)
    {
        var history = TransitionTo(LeaseStatus.Terminated, now, changedBy, reason);
        TerminatedAt = now;
        TerminationReason = reason;
        End(leftDate);
        return history;
    }

    public LeaseStatusHistory Expire(DateTimeOffset now)
    {
        var history = TransitionTo(LeaseStatus.Expired, now, changedBy: null, ExpiredReason);
        End(EndDate!.Value);
        return history;
    }

    public LeaseUtilityService AttachUtilityService(
        UtilityService service, decimal? unitPriceOverride, int quantity, Guid createdBy)
    {
        if (!service.IsActive)
        {
            throw new DomainException(
                $"Utility service '{service.Name}' is inactive and cannot be attached to a lease");
        }

        var attached = new LeaseUtilityService
        {
            LeaseId = Id,
            ServiceId = service.Id,
            Service = service,
            UnitPriceOverride = unitPriceOverride,
            CreatedBy = createdBy
        };
        attached.SetQuantity(quantity);

        UtilityServices.Add(attached);

        return attached;
    }

    private static readonly Dictionary<LeaseStatus, LeaseStatus[]> AllowedTransitions = new()
    {
        [LeaseStatus.Draft] = [LeaseStatus.Active],
        [LeaseStatus.Active] = [LeaseStatus.Expired, LeaseStatus.Terminated],
        [LeaseStatus.Expired] = [],
        [LeaseStatus.Terminated] = []
    };

    private Room RequiredRoom =>
        Room ?? throw new InvalidOperationException("Lease.Room must be loaded to change the lease status");

    private LeaseStatusHistory TransitionTo(LeaseStatus next, DateTimeOffset now, Guid? changedBy, string? reason)
    {
        if (!CanTransitionTo(next))
        {
            throw new InvalidOperationException($"Cannot transition lease from {Status} to {next}");
        }

        var history = new LeaseStatusHistory
        {
            LeaseId = Id,
            OldStatus = Status,
            NewStatus = next,
            Reason = reason,
            ChangedBy = changedBy,
            ChangedAt = now
        };

        Status = next;

        return history;
    }

    private void End(DateOnly leftDate)
    {
        MoveOutTenants(leftDate);
        RequiredRoom.RoomStatus = RoomStatus.Available;
    }

    private void MoveOutTenants(DateOnly leftDate)
    {
        foreach (var tenant in Tenants.Where(t => t.LeftDate is null || t.LeftDate > leftDate))
        {
            tenant.LeftDate = leftDate;
        }
    }
}
