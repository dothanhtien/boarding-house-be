namespace BoardingHouse.Api.Services;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string resource, string action, CancellationToken cancellationToken = default);
}
