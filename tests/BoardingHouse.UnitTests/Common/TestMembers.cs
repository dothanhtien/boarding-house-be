using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.Common;

internal static class TestMembers
{
    // OrganizationMember.Create sets only the ids; mapping tests also need the navigations
    public static OrganizationMember WithNavigations(
        OrganizationMember member, Organization organization, User user, Role role)
    {
        return TestEntities.Set(member, new { Organization = organization, User = user, Role = role });
    }
}
