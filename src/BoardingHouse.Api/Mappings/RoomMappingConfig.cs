using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class RoomMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateRoomRequest, Room>()
        .Ignore(
            dest => dest.RoomNumber,
            dest => dest.RoomCategory,
            dest => dest.RoomStatus,
            dest => dest.FloorNumber!,
            dest => dest.Area!,
            dest => dest.Capacity!,
            dest => dest.MonthlyRent!,
            dest => dest.DepositAmount!,
            dest => dest.Note!)
        .AfterMapping((src, dest) =>
        {
            src.RoomNumber.ApplyIfSet(v => dest.RoomNumber = v!);
            src.RoomCategory.ApplyIfSet(v => dest.RoomCategory = v);
            src.RoomStatus.ApplyIfSet(v => dest.RoomStatus = v);
            src.FloorNumber.ApplyIfSet(v => dest.FloorNumber = v);
            src.Area.ApplyIfSet(v => dest.Area = v);
            src.Capacity.ApplyIfSet(v => dest.Capacity = v);
            src.MonthlyRent.ApplyIfSet(v => dest.MonthlyRent = v);
            src.DepositAmount.ApplyIfSet(v => dest.DepositAmount = v);
            src.Note.ApplyIfSet(v => dest.Note = v);
        })
        .Config;
}
