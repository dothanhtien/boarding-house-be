using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.Organizations;

public record OrganizationListQuery : ListQuery
{
    public bool? IsActive { get; init; }
}
