using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Mappings;

public static class OrganizationMemberResponseMappings
{
    public static OrganizationMemberResponse ToResponse(this OrganizationMember member, User user, Role role) => new()
    {
        Id = member.Id,
        UserId = member.UserId,
        UserEmail = user.Email,
        UserFullName = user.FullName,
        RoleId = role.Id,
        RoleSlug = role.Slug,
        RoleName = role.Name,
        CreatedAt = member.CreatedAt
    };
}
