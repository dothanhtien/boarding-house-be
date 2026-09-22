using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public class UserMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<OrganizationMember, UserOrganizationResponse>()
            .Map(dest => dest.OrganizationName, src => src.Organization!.Name)
            .Map(dest => dest.RoleSlug, src => src.Role!.Slug)
            .Map(dest => dest.RoleName, src => src.Role!.Name);

        config.NewConfig<User, UserResponse>()
            .Map(dest => dest.PlatformRole, src => src.UserRoles
                .Select(ur => ur.Role)
                .OrderByDescending(r => r!.Slug == RoleSlugs.PlatformAdmin)
                .ThenBy(r => r!.Slug)
                .FirstOrDefault())
            .Map(dest => dest.Organizations, src => src.OrganizationMembers);
    }
}
