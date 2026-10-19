using BoardingHouse.Api.Exceptions;
using BoardingHouse.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Persistence;

public class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public bool HasActiveTransaction => context.Database.CurrentTransaction is not null;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw ex.ToPersistenceException();
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (HasActiveTransaction)
        {
            var nestedResult = await operation(cancellationToken);
            await SaveChangesAsync(cancellationToken);
            return nestedResult;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(async ct =>
        {
            await operation(ct);
            return true;
        }, cancellationToken);
}
