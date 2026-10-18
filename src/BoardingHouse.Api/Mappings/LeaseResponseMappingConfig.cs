using Mapster;
using BoardingHouse.Api.DTOs.Leases;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Mappings;

public class LeaseResponseMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<LeaseTenant, LeaseTenantResponse>()
            .Map(dest => dest.FullName, src => src.User!.FullName)
            .Map(dest => dest.Phone, src => src.User!.Phone);

        config.NewConfig<Lease, LeaseResponse>()
            .Map(dest => dest.PropertyId, src => src.Room!.PropertyId)
            .Map(dest => dest.RoomNumber, src => src.Room!.RoomNumber)
            .Map(dest => dest.Tenants, src => src.Tenants
                .Where(t => t.DeletedAt == null)
                .OrderByDescending(t => t.IsRepresentative)
                .ThenBy(t => t.CreatedAt)
                .ThenBy(t => t.Id));
    }
}
