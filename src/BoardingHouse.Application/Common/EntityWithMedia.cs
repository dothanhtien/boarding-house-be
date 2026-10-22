using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.Common;

public sealed record MediaLocation(string StorageKey, string MimeType);

public sealed record EntityWithMedia<TEntity>(TEntity Entity, MediaLocation? Media) where TEntity : Entity;
