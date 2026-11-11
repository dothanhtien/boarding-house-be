using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public sealed record RoomDetails(
    string RoomNumber,
    RoomCategory RoomCategory,
    int? FloorNumber,
    decimal? Area,
    int? Capacity,
    decimal? MonthlyRent,
    decimal? DepositAmount,
    string? Note);
