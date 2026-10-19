using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;

namespace BoardingHouse.Api.Repositories;

public class UserRoleRepository(AppDbContext context) : Repository<UserRole>(context), IUserRoleRepository
{
    public Task<List<Guid>> GetActiveRoleIdsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Context.UserRoles
            .Where(ur => ur.UserId == userId && ur.Role!.IsActive)
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
