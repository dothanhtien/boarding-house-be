using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Common;

public static class QueryableListExtensions
{
    public static async Task<PagedResult<TEntity>> ToPagedResultAsync<TEntity>(
        this IQueryable<TEntity> query,
        PageRequest pageRequest,
        Expression<Func<TEntity, object>> sortField,
        bool sortDescending,
        CancellationToken cancellationToken = default)
        where TEntity : Entity
    {
        query = sortDescending
            ? query.OrderByDescending(sortField).ThenBy(e => e.Id)
            : query.OrderBy(sortField).ThenBy(e => e.Id);

        var totalItems = await query.CountAsync(cancellationToken);

        var skip = (long)(pageRequest.Page - 1) * pageRequest.PageSize;
        var skipCount = skip > int.MaxValue ? int.MaxValue : (int)skip;

        var items = await query
            .Skip(skipCount)
            .Take(pageRequest.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>
        {
            Items = items,
            Page = pageRequest.Page,
            PageSize = pageRequest.PageSize,
            TotalItems = totalItems
        };
    }
}
