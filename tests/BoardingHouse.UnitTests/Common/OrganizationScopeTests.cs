using BoardingHouse.Api.Common;

namespace BoardingHouse.UnitTests.Common;

public class OrganizationScopeTests
{
    [Fact]
    public void Includes_Unrestricted_ReturnsTrueForAnyId()
    {
        var scope = new OrganizationScope { IsUnrestricted = true, OrganizationIds = new HashSet<Guid>() };

        Assert.True(scope.Includes(Guid.NewGuid()));
    }

    [Fact]
    public void Includes_RestrictedWithMatchingId_ReturnsTrue()
    {
        var organizationId = Guid.NewGuid();
        var scope = new OrganizationScope { IsUnrestricted = false, OrganizationIds = new HashSet<Guid> { organizationId } };

        Assert.True(scope.Includes(organizationId));
    }

    [Fact]
    public void Includes_RestrictedWithoutMatchingId_ReturnsFalse()
    {
        var scope = new OrganizationScope { IsUnrestricted = false, OrganizationIds = new HashSet<Guid> { Guid.NewGuid() } };

        Assert.False(scope.Includes(Guid.NewGuid()));
    }

    [Fact]
    public void None_IsRestrictedAndEmpty()
    {
        Assert.False(OrganizationScope.None.IsUnrestricted);
        Assert.Empty(OrganizationScope.None.OrganizationIds);
    }
}
