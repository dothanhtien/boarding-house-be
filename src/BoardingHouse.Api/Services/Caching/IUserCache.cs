using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Services.Caching;

public interface IUserCache
{
    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetAsync(User user, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
