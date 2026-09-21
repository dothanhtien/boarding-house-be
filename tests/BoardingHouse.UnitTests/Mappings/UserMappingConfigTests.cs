using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.UnitTests.Common;
using Mapster;

namespace BoardingHouse.UnitTests.Mappings;

public class UserMappingConfigTests
{
    public UserMappingConfigTests() => MapsterTestSupport.EnsureUserMappingRegistered();

    private static User NewUser() => new()
    {
        Email = "user@test.com",
        PasswordHash = "hash",
        FullName = "Test User",
        CreatedBy = SentinelActors.System
    };

    private static Role NewRole(string slug, string name, RoleScope scope = RoleScope.Platform) => new()
    {
        Slug = slug,
        Name = name,
        Scope = scope,
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public void Adapt_UserWithMultiplePlatformRoles_PrefersPlatformAdmin()
    {
        var user = NewUser();
        var platformAdminRole = NewRole(RoleSlugs.PlatformAdmin, "Platform Admin");
        var platformStaffRole = NewRole(RoleSlugs.PlatformStaff, "Platform Staff");
        user.UserRoles =
        [
            new UserRole { User = user, Role = platformStaffRole, CreatedBy = SentinelActors.System },
            new UserRole { User = user, Role = platformAdminRole, CreatedBy = SentinelActors.System }
        ];

        var response = user.Adapt<UserResponse>();

        Assert.Equal(RoleSlugs.PlatformAdmin, response.PlatformRole?.Slug);
    }

    [Fact]
    public void Adapt_UserWithoutPlatformRole_PlatformRoleIsNull()
    {
        var user = NewUser();
        user.UserRoles = [];

        var response = user.Adapt<UserResponse>();

        Assert.Null(response.PlatformRole);
    }

    [Fact]
    public void Adapt_UserWithMultipleOrganizationMemberships_MapsAllWithOrganizationAndRoleDetails()
    {
        var user = NewUser();
        var organizationA = new Organization { Name = "Org A", CreatedBy = SentinelActors.System };
        var organizationB = new Organization { Name = "Org B", CreatedBy = SentinelActors.System };
        var adminRole = NewRole(RoleSlugs.OrganizationAdmin, "Organization Admin", RoleScope.Organization);
        var staffRole = NewRole(RoleSlugs.OrganizationStaff, "Organization Staff", RoleScope.Organization);

        user.OrganizationMembers =
        [
            new OrganizationMember { OrganizationId = organizationA.Id, Organization = organizationA, RoleId = adminRole.Id, Role = adminRole, UserId = user.Id, User = user, CreatedBy = SentinelActors.System },
            new OrganizationMember { OrganizationId = organizationB.Id, Organization = organizationB, RoleId = staffRole.Id, Role = staffRole, UserId = user.Id, User = user, CreatedBy = SentinelActors.System }
        ];

        var response = user.Adapt<UserResponse>();

        Assert.Equal(2, response.Organizations.Count);
        Assert.Contains(response.Organizations, o =>
            o.OrganizationId == organizationA.Id && o.OrganizationName == "Org A" &&
            o.RoleId == adminRole.Id && o.RoleSlug == RoleSlugs.OrganizationAdmin && o.RoleName == "Organization Admin");
        Assert.Contains(response.Organizations, o =>
            o.OrganizationId == organizationB.Id && o.OrganizationName == "Org B" &&
            o.RoleId == staffRole.Id && o.RoleSlug == RoleSlugs.OrganizationStaff && o.RoleName == "Organization Staff");
    }

    [Fact]
    public void Adapt_UserWithoutOrganizationMemberships_OrganizationsIsEmpty()
    {
        var user = NewUser();
        user.OrganizationMembers = [];

        var response = user.Adapt<UserResponse>();

        Assert.Empty(response.Organizations);
    }
}
