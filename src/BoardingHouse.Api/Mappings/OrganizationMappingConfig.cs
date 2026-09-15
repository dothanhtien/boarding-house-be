using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class OrganizationMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateOrganizationRequest, Organization>()
        .IgnoreNullValues(true)
        .Config;
}
