using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class UserRepository(AppDbContext context) : Repository<User>(context), IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower(), cancellationToken);

    public Task<User?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        Context.Users.FirstOrDefaultAsync(u => u.Phone == phone, cancellationToken);

    public Task<EntityWithMedia<User>?> GetByIdWithAvatarAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Users
            .Where(u => u.Id == id)
            .LeftJoin(
                Context.MediaAssets.Where(m => m.EntityType == MediaAssetEntityType.UserAvatar),
                u => (Guid?)u.Id,
                m => m.EntityId,
                (u, m) => new EntityWithMedia<User>(
                    u,
                    m == null ? null : new MediaLocation(m.StorageKey, m.MimeType)))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityWithMedia<User>?> GetByIdWithRolesAndOrganizationsAsync(Guid id, CancellationToken cancellationToken = default) =>
    GetWithRolesAndOrganizationsAsync(u => u.Id == id, cancellationToken);

    public Task<EntityWithMedia<User>?> GetByEmailWithRolesAndOrganizationsAsync(string email, CancellationToken cancellationToken = default) =>
        GetWithRolesAndOrganizationsAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower(), cancellationToken);

    public Task<EntityWithMedia<User>?> GetByPhoneWithRolesAndOrganizationsAsync(string phone, CancellationToken cancellationToken = default) =>
        GetWithRolesAndOrganizationsAsync(u => u.Phone == phone, cancellationToken);

    public Task<bool> ExistsByEmailOrPhoneAsync(string? email, string? phone, CancellationToken cancellationToken = default) =>
        Context.Users.AnyAsync(u =>
            (email != null && u.Email != null && u.Email.ToLower() == email.ToLower())
            || (phone != null && u.Phone == phone),
            cancellationToken);

    public Task<bool> ExistsByEmailExcludingUserAsync(string email, Guid excludeUserId, CancellationToken cancellationToken = default) =>
        Context.Users.AnyAsync(u => u.Id != excludeUserId && u.Email != null && u.Email.ToLower() == email.ToLower(), cancellationToken);

    public Task<bool> ExistsByPhoneExcludingUserAsync(string phone, Guid excludeUserId, CancellationToken cancellationToken = default) =>
        Context.Users.AnyAsync(u => u.Id != excludeUserId && u.Phone == phone, cancellationToken);

    public async Task<PagedResult<EntityWithMedia<User>>> SearchAsync(
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
                EF.Functions.ILike(u.Email!, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(u.FullName, pattern, LikePattern.EscapeCharacter));
        }

        return await users.ToPagedResultAsync(
            pageRequest,
            sortField,
            sortOrder,
            page => page.LeftJoin(
                Context.MediaAssets.Where(m => m.EntityType == MediaAssetEntityType.UserAvatar),
                u => (Guid?)u.Id,
                m => m.EntityId,
                (u, m) => new EntityWithMedia<User>(
                    u,
                    m == null ? null : new MediaLocation(m.StorageKey, m.MimeType))),
            cancellationToken);
    }

    public Task<List<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        Context.Users.Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);

    private Task<EntityWithMedia<User>?> GetWithRolesAndOrganizationsAsync(
        Expression<Func<User, bool>> predicate, CancellationToken cancellationToken) =>
        Context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.OrganizationMembers).ThenInclude(m => m.Organization)
            .Include(u => u.OrganizationMembers).ThenInclude(m => m.Role)
            .Where(predicate)
            .LeftJoin(
                Context.MediaAssets.Where(m => m.EntityType == MediaAssetEntityType.UserAvatar),
                u => (Guid?)u.Id,
                m => m.EntityId,
                (u, m) => new EntityWithMedia<User>(
                    u,
                    m == null ? null : new MediaLocation(m.StorageKey, m.MimeType)))
            .FirstOrDefaultAsync(cancellationToken);
}
