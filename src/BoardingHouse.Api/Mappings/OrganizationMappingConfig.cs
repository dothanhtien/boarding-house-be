using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class OrganizationMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateOrganizationRequest, Organization>()
        .Ignore(
            dest => dest.Name!, dest => dest.TaxCode!, dest => dest.Phone!, dest => dest.Email!,
            dest => dest.Province!, dest => dest.District!, dest => dest.Ward!, dest => dest.Address!,
            dest => dest.IsActive!)
        .AfterMapping((src, dest) =>
        {
            src.Name.ApplyIfSet(v => dest.Name = v!);
            src.TaxCode.ApplyIfSet(v => dest.TaxCode = v);
            src.Phone.ApplyIfSet(v => dest.Phone = v);
            src.Email.ApplyIfSet(v => dest.Email = v);
            src.Province.ApplyIfSet(v => dest.Province = v);
            src.District.ApplyIfSet(v => dest.District = v);
            src.Ward.ApplyIfSet(v => dest.Ward = v);
            src.Address.ApplyIfSet(v => dest.Address = v);
            src.IsActive.ApplyIfSet(v => dest.IsActive = v);
        })
        .Config;
}
