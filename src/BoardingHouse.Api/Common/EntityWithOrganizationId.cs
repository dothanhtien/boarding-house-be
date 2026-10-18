using BoardingHouse.Domain.Common;

namespace BoardingHouse.Api.Common;

public sealed record EntityWithOrganizationId<TEntity>(TEntity Entity, Guid OrganizationId) where TEntity : Entity;
