using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class OrganizationSettingsMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateOrganizationSettingsRequest, OrganizationSettings>()
        .IgnoreNullValues(true)
        .Config;
}
