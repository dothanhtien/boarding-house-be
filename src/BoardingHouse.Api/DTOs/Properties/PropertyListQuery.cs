using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.DTOs.Properties;

public record PropertyListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public bool? IsActive { get; init; }
}
