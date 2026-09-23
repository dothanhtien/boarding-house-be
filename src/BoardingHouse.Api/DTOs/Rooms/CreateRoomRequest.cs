using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Rooms;

public record CreateRoomRequest
{
    public required Guid PropertyId { get; init; }
    public required string RoomNumber { get; init; }
    public RoomCategory RoomCategory { get; init; } = RoomCategory.Standard;
    public int? FloorNumber { get; init; }
    public decimal? Area { get; init; }
    public int? Capacity { get; init; }
    public decimal? MonthlyRent { get; init; }
    public decimal? DepositAmount { get; init; }
    public string? Note { get; init; }
}
