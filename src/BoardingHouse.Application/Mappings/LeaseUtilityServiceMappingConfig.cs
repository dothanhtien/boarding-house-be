using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Domain.Entities;
using Mapster;

namespace BoardingHouse.Application.Mappings;

public class LeaseUtilityServiceMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<LeaseUtilityService, LeaseUtilityServiceResponse>()
            .Map(dest => dest.Name, src => src.Service!.Name)
            .Map(dest => dest.Type, src => src.Service!.Type)
            .Map(dest => dest.BillingMethod, src => src.Service!.BillingMethod)
            .Map(dest => dest.Unit, src => src.Service!.Unit)
            .Map(dest => dest.DefaultUnitPrice, src => src.Service!.DefaultUnitPrice)
            .Map(dest => dest.ServiceIsActive, src => src.Service!.IsActive);
    }
}
