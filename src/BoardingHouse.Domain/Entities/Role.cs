using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Role : BaseEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }

    public required RoleScope Scope { get; set; }

    public string? Description { get; set; }
    public bool IsSystem { get; set; } = false;
    public bool IsActive { get; set; } = true;

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<UserRole> UserRoles { get; set; } = [];
}
