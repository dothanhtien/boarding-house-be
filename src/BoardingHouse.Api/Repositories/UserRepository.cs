using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class UserRepository(AppDbContext context) : Repository<User>(context), IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);

    public Task<bool> ExistsByEmailOrPhoneAsync(string email, string? phone, CancellationToken cancellationToken = default) =>
        Context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower() || (phone != null && u.Phone == phone), cancellationToken);
}
