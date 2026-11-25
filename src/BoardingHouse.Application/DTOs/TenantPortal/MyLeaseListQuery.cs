using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyLeaseListQuery : PageRequest
{
    public LeaseStatus? Status { get; init; }
}
