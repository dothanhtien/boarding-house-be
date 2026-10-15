using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class Lease : BaseEntity
{
    public const int MaxTenants = 20;

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

    public bool IsEditable => Status == LeaseStatus.Draft;
}
