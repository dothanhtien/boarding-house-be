using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.DTOs.Organizations;

public record OrganizationListQuery : ListQuery
{
    public bool? IsActive { get; init; }
}
