using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class OrganizationMember : BaseEntity
{
    public Guid OrganizationId { get; private set; }
    public Organization? Organization { get; private set; }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public Guid RoleId { get; private set; }
    public Role? Role { get; private set; }

    private OrganizationMember() { }

    public static OrganizationMember Create(Guid organizationId, Guid userId, Role role, Guid createdBy)
    {
        EnsureOrganizationScoped(role);

        return new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            RoleId = role.Id,
            CreatedBy = createdBy
        };
    }

    public void ChangeRole(Role role)
    {
        EnsureOrganizationScoped(role);
        RoleId = role.Id;
    }

    private static void EnsureOrganizationScoped(Role role)
    {
        if (role.Scope != RoleScope.Organization)
        {
            throw new DomainException("Role must be an organization-scoped role");
        }
    }
}
