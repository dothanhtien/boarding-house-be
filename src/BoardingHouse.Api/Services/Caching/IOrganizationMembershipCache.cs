namespace BoardingHouse.Api.Services.Caching;

public interface IOrganizationMembershipCache
{
    Task<List<(Guid OrganizationId, Guid RoleId)>?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetAsync(Guid userId, List<(Guid OrganizationId, Guid RoleId)> memberships, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
