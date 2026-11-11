namespace BoardingHouse.Domain.Entities;

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
    public required Guid CreatedBy { get; set; }
}
