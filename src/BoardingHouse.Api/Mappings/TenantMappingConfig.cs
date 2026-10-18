using Mapster;
using BoardingHouse.Api.DTOs.Tenants;
using BoardingHouse.Domain.Entities;

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
