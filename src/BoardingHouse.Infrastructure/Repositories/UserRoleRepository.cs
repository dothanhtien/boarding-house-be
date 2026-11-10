using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class UserRoleRepository(AppDbContext context) : IUserRoleRepository
{
    public Task<List<Guid>> GetActiveRoleIdsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.UserRoles
            .Where(ur => ur.UserId == userId && ur.Role!.IsActive)
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
