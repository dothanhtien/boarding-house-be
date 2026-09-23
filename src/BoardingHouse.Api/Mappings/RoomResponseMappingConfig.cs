using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public class RoomResponseMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Room, RoomResponse>()
            .Map(dest => dest.Amenities, src => src.Amenities
                .Where(a => a.DeletedAt == null)
                .OrderBy(a => a.Name)
                .ThenBy(a => a.CreatedAt)
                .ThenBy(a => a.Id));
    }
}
