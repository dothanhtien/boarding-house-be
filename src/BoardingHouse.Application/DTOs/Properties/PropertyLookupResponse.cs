namespace BoardingHouse.Application.DTOs.Properties;

public record PropertyLookupResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
