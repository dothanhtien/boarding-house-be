namespace BoardingHouse.Api.Services;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string resource, string action, CancellationToken cancellationToken = default);

    Task<List<(string Resource, string Action)>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default);
}
