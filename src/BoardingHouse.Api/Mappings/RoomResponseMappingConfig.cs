using System.Linq.Expressions;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public class RoomResponseMappingConfig : IRegister
{
    private static readonly Expression<Func<Room, IEnumerable<RoomAmenity>>> OrderedAmenities = src => src.Amenities
        .Where(a => a.DeletedAt == null)
        .OrderBy(a => a.Name)
        .ThenBy(a => a.CreatedAt)
        .ThenBy(a => a.Id);

    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Room, RoomResponse>()
            .Map(dest => dest.Amenities, OrderedAmenities);

        config.NewConfig<Room, RoomDetailsResponse>()
            .Map(dest => dest.Amenities, OrderedAmenities)
            .Map(dest => dest.Assets, src => src.Assets
                .Where(a => a.DeletedAt == null)
                .OrderBy(a => a.Name)
                .ThenBy(a => a.CreatedAt)
                .ThenBy(a => a.Id))
            .Ignore(dest => dest.Media);
    }
}
