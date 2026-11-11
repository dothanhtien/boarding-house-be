using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class LeaseStatusHistory : Entity
{
    public Guid LeaseId { get; private set; }
    public Lease? Lease { get; private set; }

    public LeaseStatus? OldStatus { get; private set; }
    public LeaseStatus NewStatus { get; private set; }
    public string? Reason { get; private set; }

    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private LeaseStatusHistory() { }

    public LeaseStatusHistory(
        Guid leaseId, LeaseStatus? oldStatus, LeaseStatus newStatus, string? reason, Guid? changedBy, DateTimeOffset changedAt)
    {
        LeaseId = leaseId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Reason = reason;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }
}
