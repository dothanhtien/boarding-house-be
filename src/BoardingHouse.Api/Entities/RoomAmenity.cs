using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities;

public class RoomAmenity : BaseEntity
{
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public required string Name { get; set; }

    // Null = not countable (e.g. WiFi, balcony)
    public int? Quantity { get; set; }

    // Icon key (e.g. wifi, air-conditioner) mapped to an icon set by the frontend
    public string? Icon { get; set; }
}
