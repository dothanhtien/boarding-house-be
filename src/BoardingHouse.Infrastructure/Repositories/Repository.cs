using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class Repository<T>(AppDbContext context) : IRepository<T> where T : BaseEntity
{
    protected AppDbContext Context => context;

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Set<T>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Set<T>().AnyAsync(e => e.Id == id, cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
        await context.Set<T>().AddAsync(entity, cancellationToken);

    // Soft-delete is enforced centrally by AuditableEntitySaveChangesInterceptor, which
    // converts this EntityState.Deleted into a soft delete for any ISoftDeletable entity —
    // including entities removed directly via context.Set<T>().Remove() elsewhere.
    public void SoftDelete(T entity) => context.Set<T>().Remove(entity);
}
