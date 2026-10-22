using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class LeaseStatusHistory : Entity
{
    public Guid LeaseId { get; set; }
    public Lease? Lease { get; set; }

    public LeaseStatus? OldStatus { get; set; }
    public required LeaseStatus NewStatus { get; set; }
    public string? Reason { get; set; }

    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
