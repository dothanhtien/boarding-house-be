using BoardingHouse.Api.DTOs.UtilityServices;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class UtilityServiceMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateUtilityServiceRequest, UtilityService>()
        .Ignore(
            dest => dest.Name,
            dest => dest.Unit,
            dest => dest.DefaultUnitPrice!,
            dest => dest.IsActive)
        .AfterMapping((src, dest) =>
        {
            src.Name.ApplyIfSet(v => dest.Name = v!);
            src.Unit.ApplyIfSet(v => dest.Unit = v!);
            src.DefaultUnitPrice.ApplyIfSet(v => dest.DefaultUnitPrice = v);
            src.IsActive.ApplyIfSet(v => dest.IsActive = v);
        })
        .Config;
}
