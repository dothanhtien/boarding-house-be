namespace BoardingHouse.Application.DTOs.Properties;

public record PropertyLookupQuery
{
    public Guid? OrganizationId { get; init; }
}
