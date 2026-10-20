using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

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

    public Task<Role?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Context.Roles.FirstOrDefaultAsync(r => r.Slug == slug && r.IsActive, cancellationToken);
}
