using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class OrganizationTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Role OwnerRole = NewRole("organization_admin");
    private static readonly Role StaffRole = NewRole("organization_staff");

    private static Role NewRole(string slug, RoleScope scope = RoleScope.Organization) =>
        new() { Name = slug, Slug = slug, Scope = scope, CreatedBy = SentinelActors.System };

    private static OrganizationDetails Details(string name = "Org") =>
        new(name, TaxCode: null, Phone: null, Email: null, Province: null, District: null, Ward: null, Address: null, IsActive: true);

    private static Organization NewOrganization(Guid ownerUserId) =>
        Organization.Create(Details(), ownerUserId, OwnerRole, ActorId);

    [Fact]
    public void Create_AddsTheOwnerAsMember()
    {
        var ownerId = Guid.NewGuid();

        var organization = NewOrganization(ownerId);

        var owner = Assert.Single(organization.Members);
        Assert.Equal((organization.Id, ownerId, OwnerRole.Id), (owner.OrganizationId, owner.UserId, owner.RoleId));
    }

    [Fact]
    public void Update_BlankName_Throws()
    {
        var organization = NewOrganization(Guid.NewGuid());

        Assert.Throws<DomainException>(() => organization.Update(Details(" ")));
    }

    [Fact]
    public void AddMember_ExistingUser_ThrowsConflict()
    {
        var ownerId = Guid.NewGuid();
        var organization = NewOrganization(ownerId);

        var ex = Assert.Throws<DomainException>(() => organization.AddMember(ownerId, StaffRole, ActorId));

        Assert.Equal((DomainErrorKind.Conflict, "User is already a member of this organization"), (ex.Kind, ex.Message));
    }

    [Fact]
    public void AddMember_PlatformRole_Throws()
    {
        var organization = NewOrganization(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            organization.AddMember(Guid.NewGuid(), NewRole("platform_staff", RoleScope.Platform), ActorId));
    }

    [Fact]
    public void ChangeMemberRole_Owner_ThrowsConflict()
    {
        var organization = NewOrganization(Guid.NewGuid());
        var owner = organization.Members.Single();

        var ex = Assert.Throws<DomainException>(() => organization.ChangeMemberRole(owner.Id, StaffRole, OwnerRole.Id));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void ChangeMemberRole_UnknownMember_ThrowsNotFound()
    {
        var organization = NewOrganization(Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() => organization.ChangeMemberRole(Guid.NewGuid(), StaffRole, OwnerRole.Id));

        Assert.Equal(DomainErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void RemoveMember_Staff_RemovesFromCollection()
    {
        var organization = NewOrganization(Guid.NewGuid());
        var staff = organization.AddMember(Guid.NewGuid(), StaffRole, ActorId);

        organization.RemoveMember(staff.Id, OwnerRole.Id);

        Assert.DoesNotContain(staff, organization.Members);
    }

    [Fact]
    public void TransferOwnership_ToNewUser_AddsOwnerAndDemotesPreviousOwnerToStaff()
    {
        var previousOwnerId = Guid.NewGuid();
        var organization = NewOrganization(previousOwnerId);
        var newOwnerId = Guid.NewGuid();

        var affected = organization.TransferOwnership(newOwnerId, OwnerRole, StaffRole, ActorId);

        Assert.Equal([newOwnerId, previousOwnerId], affected);
        Assert.Equal(OwnerRole.Id, organization.Members.Single(m => m.UserId == newOwnerId).RoleId);
        Assert.Equal(StaffRole.Id, organization.Members.Single(m => m.UserId == previousOwnerId).RoleId);
    }

    [Fact]
    public void TransferOwnership_ToExistingStaffWithoutKeepingPreviousOwner_PromotesAndRemovesPreviousOwner()
    {
        var previousOwnerId = Guid.NewGuid();
        var organization = NewOrganization(previousOwnerId);
        var staff = organization.AddMember(Guid.NewGuid(), StaffRole, ActorId);

        organization.TransferOwnership(staff.UserId, OwnerRole, staffRole: null, ActorId);

        var owner = Assert.Single(organization.Members);
        Assert.Equal((staff.UserId, OwnerRole.Id), (owner.UserId, owner.RoleId));
    }

    [Fact]
    public void TransferOwnership_ToCurrentOwner_ChangesNothing()
    {
        var ownerId = Guid.NewGuid();
        var organization = NewOrganization(ownerId);

        var affected = organization.TransferOwnership(ownerId, OwnerRole, StaffRole, ActorId);

        Assert.Equal([ownerId], affected);
        Assert.Equal(OwnerRole.Id, Assert.Single(organization.Members).RoleId);
    }
}
