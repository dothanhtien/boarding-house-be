using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.DTOs.Rooms;

public record UpdateRoomAmenityRequest
{
    // Null = add a new amenity; set = update (or delete, with IsDeleted) an existing amenity of this room.
    public Guid? Id { get; init; }

    // Null = leave unchanged (existing amenity only; required when adding).
    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Quantity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Icon { get; init; }

    public bool IsDeleted { get; init; }
}
