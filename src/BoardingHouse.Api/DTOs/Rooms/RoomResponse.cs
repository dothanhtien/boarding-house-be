using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Rooms;

public record RoomResponse
{
    public required Guid Id { get; init; }
    public required Guid PropertyId { get; init; }
    public required string RoomNumber { get; init; }
    public required RoomCategory RoomCategory { get; init; }
    public required RoomStatus RoomStatus { get; init; }
    public int? FloorNumber { get; init; }
    public decimal? Area { get; init; }
    public int? Capacity { get; init; }
    public decimal? MonthlyRent { get; init; }
    public decimal? DepositAmount { get; init; }
    public string? Note { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public required List<RoomAmenityResponse> Amenities { get; init; }
}
