using Mapster;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.UnitTests.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Users;

namespace BoardingHouse.UnitTests.Mappings;

public class UserMappingConfigTests
{
    public UserMappingConfigTests() => MapsterTestSupport.EnsureUserMappingRegistered();

    private static User NewUser() => TestEntities.New<User>(new
    {
        Email = "user@test.com",
        PasswordHash = "hash",
        FullName = "Test User",
        CreatedBy = SentinelActors.System
    });

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
        TestEntities.Set(user, new
        {
            UserRoles = new List<UserRole>
            {
                new() { User = user, Role = platformStaffRole, CreatedBy = SentinelActors.System },
                new() { User = user, Role = platformAdminRole, CreatedBy = SentinelActors.System }
            }
        });

        var response = user.Adapt<UserResponse>();

        Assert.Equal(RoleSlugs.PlatformAdmin, response.PlatformRole?.Slug);
    }

    [Fact]
    public void Adapt_UserWithoutPlatformRole_PlatformRoleIsNull()
    {
        var user = NewUser();
        TestEntities.Set(user, new { UserRoles = new List<UserRole>() });

        var response = user.Adapt<UserResponse>();

        Assert.Null(response.PlatformRole);
    }

    [Fact]
    public void Adapt_UserWithMultipleOrganizationMemberships_MapsAllWithOrganizationAndRoleDetails()
    {
        var user = NewUser();
        var organizationA = TestEntities.New<Organization>(new { Name = "Org A", CreatedBy = SentinelActors.System });
        var organizationB = TestEntities.New<Organization>(new { Name = "Org B", CreatedBy = SentinelActors.System });
        var adminRole = NewRole(RoleSlugs.OrganizationAdmin, "Organization Admin", RoleScope.Organization);
        var staffRole = NewRole(RoleSlugs.OrganizationStaff, "Organization Staff", RoleScope.Organization);

        TestEntities.Set(user, new
        {
            OrganizationMembers = new List<OrganizationMember>
            {
                TestMembers.WithNavigations(OrganizationMember.Create(organizationA.Id, user.Id, adminRole, SentinelActors.System), organizationA, user, adminRole),
                TestMembers.WithNavigations(OrganizationMember.Create(organizationB.Id, user.Id, staffRole, SentinelActors.System), organizationB, user, staffRole)
            }
        });

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
        TestEntities.Set(user, new { OrganizationMembers = new List<OrganizationMember>() });

        var response = user.Adapt<UserResponse>();

        Assert.Empty(response.Organizations);
    }
}
