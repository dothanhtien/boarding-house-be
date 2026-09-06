using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IRoleRepository : IRepository<Role>
{
    Task<Role?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
