namespace BoardingHouse.Application.Persistence;

public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
    void DiscardChanges();
}
