using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

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
