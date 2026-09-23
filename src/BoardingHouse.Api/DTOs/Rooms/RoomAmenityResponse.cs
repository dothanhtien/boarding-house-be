namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomAmenityResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int? Quantity { get; init; }
    public required string? Icon { get; init; }
}
