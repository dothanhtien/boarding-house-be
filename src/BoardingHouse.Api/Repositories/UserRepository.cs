using System.Linq.Expressions;
using BoardingHouse.Api.Common;
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

    public Task<bool> ExistsByEmailExcludingUserAsync(string email, Guid excludeUserId, CancellationToken cancellationToken = default) =>
        Context.Users.AnyAsync(u => u.Id != excludeUserId && u.Email.ToLower() == email.ToLower(), cancellationToken);

    public Task<bool> ExistsByPhoneExcludingUserAsync(string phone, Guid excludeUserId, CancellationToken cancellationToken = default) =>
        Context.Users.AnyAsync(u => u.Id != excludeUserId && u.Phone == phone, cancellationToken);

    public async Task<PagedResult<User>> SearchAsync(
        bool? isActive,
        string? search,
        PageRequest pageRequest,
        Expression<Func<User, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var users = Context.Users.AsQueryable();

        if (isActive is not null)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            users = users.Where(u =>
                EF.Functions.ILike(u.Email, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(u.FullName, pattern, LikePattern.EscapeCharacter));
        }

        return await users.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }
}
