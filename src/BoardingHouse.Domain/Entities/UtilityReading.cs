using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class UtilityReading : Entity, ISoftDeletable
{
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }

    public Guid ServiceId { get; private set; }
    public UtilityService? Service { get; private set; }

    public DateOnly ReadOn { get; private set; }

    public BillingMonth ReadingMonth { get; private set; }

    public Guid? BilledLeaseId { get; private set; }
    public Lease? BilledLease { get; private set; }

    public decimal? PreviousValue { get; private set; }
    public decimal CurrentValue { get; private set; }

    public decimal? Consumption { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public bool IsDeleted => DeletedAt.HasValue;

    private UtilityReading() { }

    public static UtilityReading Record(Guid roomId,
        Guid serviceId,
        DateOnly readOn,
        BillingMonth readingMonth,
        UtilityReading? latest,
        decimal? initialPreviousValue,
        decimal currentValue,
        Guid? billedLeaseId,
        DateTimeOffset now,
        Guid createdBy)
    {
        if (latest is not null && latest.ReadingMonth >= readingMonth)
        {
            throw new DomainException(latest.ReadingMonth == readingMonth
                ? "A reading already exists for this month — delete it to enter it again"
                : $"A reading for a later month ({latest.ReadingMonth}) already exists");
        }

        if (latest is not null && latest.ReadOn > readOn)
        {
            throw new DomainException($"A later reading ({latest.ReadOn:yyyy-MM-dd}) already exists on this meter");
        }

        var previousValue = latest?.CurrentValue ?? initialPreviousValue;
        if (currentValue < previousValue)
        {
            throw new DomainException(
                $"CurrentValue must not be less than the previous reading ({previousValue})",
                field: nameof(CurrentValue));
        }

        return new UtilityReading
        {
            RoomId = roomId,
            ServiceId = serviceId,
            ReadOn = readOn,
            ReadingMonth = readingMonth,
            BilledLeaseId = billedLeaseId,
            PreviousValue = previousValue,
            CurrentValue = currentValue,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }
}
