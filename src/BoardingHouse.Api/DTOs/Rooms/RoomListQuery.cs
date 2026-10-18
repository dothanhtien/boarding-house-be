using BoardingHouse.Api.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public RoomCategory? RoomCategory { get; init; }
    public RoomStatus? RoomStatus { get; init; }
}
