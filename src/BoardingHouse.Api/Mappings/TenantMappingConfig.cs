using BoardingHouse.Api.DTOs.Tenants;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public class TenantMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Lease, TenantLeaseResponse>()
            .Map(dest => dest.LeaseId, src => src.Id)
            .Map(dest => dest.RoomNumber, src => src.Room!.RoomNumber);
    }
}
