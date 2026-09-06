namespace BoardingHouse.Api.Services.Caching;

public interface IRolePermissionCache
{
    Task<List<(string Resource, string Action)>?> GetAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task SetAsync(Guid roleId, List<(string Resource, string Action)> permissions, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid roleId, CancellationToken cancellationToken = default);
}
