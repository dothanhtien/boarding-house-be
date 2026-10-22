using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.Properties;

public record PropertyListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public bool? IsActive { get; init; }
}
