using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Repositories;

public interface IRepository<T> where T : BaseEntity
{
    Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void SoftDelete(T entity);
    Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
