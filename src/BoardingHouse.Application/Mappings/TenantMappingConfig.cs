using Mapster;
using BoardingHouse.Application.DTOs.Tenants;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Mappings;

public class TenantMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Lease, TenantLeaseResponse>()
            .Map(dest => dest.LeaseId, src => src.Id)
            .Map(dest => dest.RoomNumber, src => src.Room!.RoomNumber);
    }
}
