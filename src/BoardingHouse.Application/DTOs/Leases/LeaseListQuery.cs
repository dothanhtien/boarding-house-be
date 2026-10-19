using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.Leases;

public record LeaseListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public Guid? RoomId { get; init; }
    public LeaseStatus? Status { get; init; }
    public Guid? TenantUserId { get; init; }
}
