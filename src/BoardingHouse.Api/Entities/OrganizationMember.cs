using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities;

public class OrganizationMember : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}
