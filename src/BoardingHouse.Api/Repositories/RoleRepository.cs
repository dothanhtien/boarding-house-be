using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class RoleRepository(AppDbContext context) : Repository<Role>(context), IRoleRepository
{
    public Task<List<(string Resource, string Action)>> GetPermissionsByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        Context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => new ValueTuple<string, string>(rp.Permission!.Resource, rp.Permission!.Action))
            .Distinct()
            .ToListAsync(cancellationToken);
}
