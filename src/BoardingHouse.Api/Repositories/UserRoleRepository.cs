using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class UserRoleRepository(AppDbContext context) : Repository<UserRole>(context), IUserRoleRepository
{
    public Task<List<(string Resource, string Action)>> GetPermissionsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Context.UserRoles
            .Where(ur => ur.UserId == userId && ur.Role!.IsActive)
            .SelectMany(ur => ur.Role!.RolePermissions)
            .Select(rp => new ValueTuple<string, string>(rp.Permission!.Resource, rp.Permission!.Action))
            .Distinct()
            .ToListAsync(cancellationToken);
}
