using BoardingHouse.Api.Common;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class Repository<T>(AppDbContext context) : IRepository<T> where T : BaseEntity
{
    protected AppDbContext Context => context;

    public async Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await context.Set<T>().ToListAsync(cancellationToken);

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Set<T>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
        await context.Set<T>().AddAsync(entity, cancellationToken);

    public void Update(T entity) => context.Set<T>().Update(entity);

    // Soft-delete is enforced centrally by AuditableEntitySaveChangesInterceptor, which
    // converts this EntityState.Deleted into a soft delete for any ISoftDeletable entity —
    // including entities removed directly via context.Set<T>().Remove() elsewhere.
    public void SoftDelete(T entity) => context.Set<T>().Remove(entity);

    // Bypasses the change tracker (and therefore AuditableEntitySaveChangesInterceptor) by
    // issuing a DELETE directly against the database, so this always hard-deletes even for
    // ISoftDeletable entities. Executes immediately, independent of SaveChangesAsync.
    public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<T>().IgnoreQueryFilters().Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken);
}
