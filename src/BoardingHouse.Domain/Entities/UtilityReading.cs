using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class UtilityReading : Entity, ISoftDeletable
{
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid ServiceId { get; set; }
    public UtilityService? Service { get; set; }

    public DateOnly ReadOn { get; set; }

    public DateOnly ReadingMonth { get; set; }

    public Guid? BilledLeaseId { get; set; }
    public Lease? BilledLease { get; set; }

    public decimal? PreviousValue { get; set; }
    public decimal CurrentValue { get; set; }

    public decimal? Consumption { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public required Guid CreatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public bool IsDeleted => DeletedAt.HasValue;
}
