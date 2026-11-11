using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

// One item of a change on a room's amenities
// Id null = add (Name required); set = update (or delete, with IsDeleted) an existing amenity
// Name null = leave unchanged; Quantity/Icon not set = leave unchanged, set to null = clear
public sealed record AmenityChange
{
    public Guid? Id { get; init; }
    public string? Name { get; init; }
    public Change<int?> Quantity { get; init; }
    public Change<string?> Icon { get; init; }
    public bool IsDeleted { get; init; }
}
