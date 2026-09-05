using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities
{
    public class Permission : AuditableEntity
    {
        public required string Resource { get; set; }
        public required string Action { get; set; }
        public string? Description { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; } = [];
    }
}
