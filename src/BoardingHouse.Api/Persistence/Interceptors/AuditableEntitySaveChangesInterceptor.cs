using BoardingHouse.Api.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BoardingHouse.Api.Persistence.Interceptors;

public class AuditableEntitySaveChangesInterceptor(
    ICurrentUserAccessor currentUserAccessor,
    ILogger<AuditableEntitySaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAuditInfo(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInfo(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditInfo(DbContext? context)
    {
        if (context is null) return;

        var now = DateTimeOffset.UtcNow;
        var userId = currentUserAccessor.User?.Id;
        var warnedEntities = new HashSet<object>();

        ConvertDeletesToSoftDeletes(context, now, userId, warnedEntities);
        UpdateAuditFields(context, now, userId, warnedEntities);
    }

    private void ConvertDeletesToSoftDeletes(DbContext context, DateTimeOffset now, Guid? userId, HashSet<object> warnedEntities)
    {
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                var deletedByExplicitlySet = IsExplicitlySet(entry.Property(e => e.DeletedBy));
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = now;
                if (!deletedByExplicitlySet)
                {
                    if (userId is not null)
                    {
                        entry.Entity.DeletedBy = userId;
                    }
                    else if (warnedEntities.Add(entry.Entity))
                    {
                        logger.LogWarning("Soft-deleting {EntityType} without a current user — DeletedBy left null", entry.Entity.GetType().Name);
                    }
                }
            }
        }
    }

    private void UpdateAuditFields(DbContext context, DateTimeOffset now, Guid? userId, HashSet<object> warnedEntities)
    {
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    if (!IsExplicitlySet(entry.Property(e => e.UpdatedBy)))
                    {
                        if (userId is not null)
                        {
                            entry.Entity.UpdatedBy = userId;
                        }
                        else if (warnedEntities.Add(entry.Entity))
                        {
                            logger.LogWarning("Updating {EntityType} without a current user — UpdatedBy left null", entry.Entity.GetType().Name);
                        }
                    }
                    break;
            }
        }
    }

    private static bool IsExplicitlySet(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry property) =>
        !Equals(property.CurrentValue, property.OriginalValue);
}
