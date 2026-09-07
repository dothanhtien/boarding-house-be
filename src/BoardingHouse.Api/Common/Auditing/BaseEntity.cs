namespace BoardingHouse.Api.Common;

public abstract class BaseEntity : AuditableEntity, ISoftDeletable
{
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public bool IsDeleted => DeletedAt.HasValue;
}
