using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.Rooms;

public record RoomListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public RoomCategory? RoomCategory { get; init; }
    public RoomStatus? RoomStatus { get; init; }
}
