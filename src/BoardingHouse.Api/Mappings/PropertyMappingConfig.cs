using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class PropertyMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdatePropertyRequest, Property>()
        .Ignore(
            dest => dest.Name!,
            dest => dest.Description!,
            dest => dest.Province!,
            dest => dest.District!,
            dest => dest.Ward!,
            dest => dest.Address!,
            dest => dest.IsActive!,
            dest => dest.DefaultBillingDay!,
            dest => dest.LateFeeType!,
            dest => dest.LateFeeValue!,
            dest => dest.LateFeeGraceDays!)
        .AfterMapping((src, dest) =>
        {
            src.Name.ApplyIfSet(v => dest.Name = v!);
            src.Description.ApplyIfSet(v => dest.Description = v);
            src.Province.ApplyIfSet(v => dest.Province = v);
            src.District.ApplyIfSet(v => dest.District = v);
            src.Ward.ApplyIfSet(v => dest.Ward = v);
            src.Address.ApplyIfSet(v => dest.Address = v);
            src.IsActive.ApplyIfSet(v => dest.IsActive = v);
            src.DefaultBillingDay.ApplyIfSet(v => dest.DefaultBillingDay = v);
            src.LateFeeType.ApplyIfSet(v => dest.LateFeeType = v);
            src.LateFeeValue.ApplyIfSet(v => dest.LateFeeValue = v);
            src.LateFeeGraceDays.ApplyIfSet(v => dest.LateFeeGraceDays = v);
        })
        .Config;
}
