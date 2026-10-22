using System.Linq.Expressions;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<EntityWithMedia<User>?> GetByIdWithAvatarAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EntityWithMedia<User>?> GetByEmailWithRolesAndOrganizationsAsync(string email, CancellationToken cancellationToken = default);
    Task<EntityWithMedia<User>?> GetByPhoneWithRolesAndOrganizationsAsync(string phone, CancellationToken cancellationToken = default);
    Task<EntityWithMedia<User>?> GetByIdWithRolesAndOrganizationsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailOrPhoneAsync(string? email, string? phone, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailExcludingUserAsync(string email, Guid excludeUserId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneExcludingUserAsync(string phone, Guid excludeUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<EntityWithMedia<User>>> SearchAsync(
        bool? isActive,
        string? search,
        PageRequest pageRequest,
        Expression<Func<User, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
    Task<List<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
