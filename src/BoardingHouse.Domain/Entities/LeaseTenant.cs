using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class LeaseTenant : BaseEntity
{
    public Guid LeaseId { get; set; }
    public Lease? Lease { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public bool IsRepresentative { get; set; }

    // Null = moved in on Lease.StartDate.
    public DateOnly? JoinedDate { get; set; }

    // Null = still living there.
    public DateOnly? LeftDate { get; set; }
}
