using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class RoleRepository(AppDbContext context) : Repository<Role>(context), IRoleRepository
{
    public Task<Role?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Context.Roles.FirstOrDefaultAsync(r => r.Slug == slug, cancellationToken);
}
