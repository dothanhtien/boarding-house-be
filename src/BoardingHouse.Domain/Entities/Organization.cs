using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class Organization : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string? TaxCode { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Province { get; private set; }
    public string? District { get; private set; }
    public string? Ward { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<OrganizationMember> _members = [];

    public IReadOnlyCollection<OrganizationMember> Members => _members;

    public OrganizationDetails Details => new(Name, TaxCode, Phone, Email, Province, District, Ward, Address, IsActive);

    private Organization() { }

    public static Organization Create(OrganizationDetails details, Guid ownerUserId, Role ownerRole, Guid createdBy)
    {
        var organization = new Organization { CreatedBy = createdBy };
        organization.Update(details);
        organization._members.Add(OrganizationMember.Create(organization.Id, ownerUserId, ownerRole, createdBy));
        return organization;
    }

    public void Update(OrganizationDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
        {
            throw new DomainException("Name is required");
        }

        Name = details.Name;
        TaxCode = details.TaxCode;
        Phone = details.Phone;
        Email = details.Email;
        Province = details.Province;
        District = details.District;
        Ward = details.Ward;
        Address = details.Address;
        IsActive = details.IsActive;
    }

    public OrganizationMember AddMember(Guid userId, Role role, Guid createdBy)
    {
        var member = OrganizationMember.Create(Id, userId, role, createdBy);

        if (_members.Any(m => m.UserId == userId))
        {
            throw DomainException.Conflict("User is already a member of this organization");
        }

        _members.Add(member);
        return member;
    }

    public OrganizationMember ChangeMemberRole(Guid memberId, Role role, Guid ownerRoleId)
    {
        var member = RequiredNonOwnerMember(memberId, ownerRoleId);
        member.ChangeRole(role);
        return member;
    }

    public OrganizationMember RemoveMember(Guid memberId, Guid ownerRoleId)
    {
        var member = RequiredNonOwnerMember(memberId, ownerRoleId);
        _members.Remove(member);
        return member;
    }

    public IReadOnlyList<Guid> TransferOwnership(Guid newOwnerUserId, Role ownerRole, Role? staffRole, Guid actor)
    {
        var newOwner = _members.FirstOrDefault(m => m.UserId == newOwnerUserId);
        if (newOwner is null)
        {
            _members.Add(OrganizationMember.Create(Id, newOwnerUserId, ownerRole, actor));
        }
        else if (newOwner.RoleId != ownerRole.Id)
        {
            newOwner.ChangeRole(ownerRole);
        }

        var previousOwners = _members.Where(m => m.RoleId == ownerRole.Id && m.UserId != newOwnerUserId).ToList();
        foreach (var previousOwner in previousOwners)
        {
            if (staffRole is not null)
            {
                previousOwner.ChangeRole(staffRole);
            }
            else
            {
                _members.Remove(previousOwner);
            }
        }

        return [newOwnerUserId, .. previousOwners.Select(m => m.UserId)];
    }

    private OrganizationMember RequiredNonOwnerMember(Guid memberId, Guid ownerRoleId)
    {
        var member = _members.FirstOrDefault(m => m.Id == memberId)
            ?? throw DomainException.NotFound($"Organization member '{memberId}' not found");

        if (member.RoleId == ownerRoleId)
        {
            throw DomainException.Conflict(
                "The organization owner cannot be changed or removed here; transfer ownership via the organization update endpoint");
        }

        return member;
    }
}
