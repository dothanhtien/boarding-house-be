using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public sealed record TenantChange
{
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public bool? IsRepresentative { get; init; }
    public Change<DateOnly?> JoinedDate { get; init; }
    public Change<DateOnly?> LeftDate { get; init; }
    public bool IsDeleted { get; init; }
}
