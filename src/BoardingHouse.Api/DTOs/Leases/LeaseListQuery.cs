using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Leases;

public record LeaseListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public Guid? RoomId { get; init; }
    public LeaseStatus? Status { get; init; }
    public Guid? TenantUserId { get; init; }
}
