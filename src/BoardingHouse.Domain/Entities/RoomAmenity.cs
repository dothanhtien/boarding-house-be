using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class RoomAmenity : BaseEntity
{
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }
    public string Name { get; private set; } = null!;

    // Null = not countable (e.g. WiFi, balcony)
    public int? Quantity { get; private set; }

    // Icon key (e.g. wifi, air-conditioner) mapped to an icon set by the frontend
    public string? Icon { get; private set; }

    private RoomAmenity() { }

    public RoomAmenity(Guid roomId, string? name, int? quantity, string? icon, Guid createdBy)
    {
        RoomId = roomId;
        Rename(name);
        SetQuantity(quantity);
        Icon = icon;
        CreatedBy = createdBy;
    }

    public void Rename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required");
        }

        Name = name;
    }

    public void SetQuantity(int? quantity)
    {
        if (quantity < 1)
        {
            throw new DomainException("Quantity must be at least 1");
        }

        Quantity = quantity;
    }

    public void SetIcon(string? icon) => Icon = icon;
}
