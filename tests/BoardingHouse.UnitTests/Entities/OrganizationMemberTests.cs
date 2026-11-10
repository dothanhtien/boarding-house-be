using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class OrganizationMemberTests
{
    private static Role NewRole(RoleScope scope) =>
        new() { Name = "Role", Slug = $"role-{Guid.NewGuid():N}", Scope = scope, CreatedBy = SentinelActors.System };

    [Fact]
    public void Create_OrganizationRole_SetsIdsOnly()
    {
        var role = NewRole(RoleScope.Organization);

        var member = OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), role, SentinelActors.System);

        Assert.Equal(role.Id, member.RoleId);
        Assert.Null(member.Role);
    }

    [Fact]
    public void Create_PlatformRole_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), NewRole(RoleScope.Platform), SentinelActors.System));

        Assert.Equal("Role must be an organization-scoped role", ex.Message);
    }

    [Fact]
    public void ChangeRole_PlatformRole_ThrowsAndKeepsRole()
    {
        var role = NewRole(RoleScope.Organization);
        var member = OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), role, SentinelActors.System);

        Assert.Throws<DomainException>(() => member.ChangeRole(NewRole(RoleScope.Platform)));

        Assert.Equal(role.Id, member.RoleId);
    }
}
