namespace BoardingHouse.Domain.Common;

public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
    bool IsDeleted { get; }
}
