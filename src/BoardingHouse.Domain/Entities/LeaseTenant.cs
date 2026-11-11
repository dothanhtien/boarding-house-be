using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class LeaseTenant : BaseEntity
{
    public Guid LeaseId { get; private set; }
    public Lease? Lease { get; private set; }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public bool IsRepresentative { get; private set; }

    // Null = moved in on Lease.StartDate.
    public DateOnly? JoinedDate { get; private set; }

    // Null = still living there.
    public DateOnly? LeftDate { get; private set; }

    private LeaseTenant() { }

    public LeaseTenant(Guid leaseId, User user, bool isRepresentative, DateOnly? joinedDate, Guid createdBy)
    {
        LeaseId = leaseId;
        UserId = user.Id;
        User = user;
        IsRepresentative = isRepresentative;
        JoinedDate = joinedDate;
        CreatedBy = createdBy;
    }

    public void SetRepresentative(bool isRepresentative) => IsRepresentative = isRepresentative;

    public void SetJoinedDate(DateOnly? joinedDate) => JoinedDate = joinedDate;

    public void SetLeftDate(DateOnly? leftDate) => LeftDate = leftDate;
}
