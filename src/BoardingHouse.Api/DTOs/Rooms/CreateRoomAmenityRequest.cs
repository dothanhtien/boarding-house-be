namespace BoardingHouse.Api.DTOs.Rooms;

public record CreateRoomAmenityRequest
{
    public required string Name { get; init; }
    // Null = not countable (e.g. WiFi, balcony)
    public int? Quantity { get; init; }

    // Icon key (e.g. wifi, air-conditioner) mapped to an icon set by the frontend
    public string? Icon { get; init; }
}
