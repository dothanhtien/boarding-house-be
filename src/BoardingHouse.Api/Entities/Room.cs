using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class Room : BaseEntity
{
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public required string RoomNumber { get; set; }
    public RoomCategory RoomCategory { get; set; } = RoomCategory.Standard;
    public RoomStatus RoomStatus { get; set; } = RoomStatus.Available;
    public int? FloorNumber { get; set; }
    public decimal? Area { get; set; }
    public int? Capacity { get; set; }
    public decimal? MonthlyRent { get; set; }
    public decimal? DepositAmount { get; set; }
    public string? Note { get; set; }
}
