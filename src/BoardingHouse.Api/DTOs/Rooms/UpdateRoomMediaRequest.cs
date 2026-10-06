using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.DTOs.Rooms;

public record UpdateRoomMediaRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<List<Guid>> Order { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> CoverMediaId { get; init; }
}
