using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Common;

public static class QueryableListExtensions
{
    public static Task<PagedResult<TEntity>> ToPagedResultAsync<TEntity>(
        this IQueryable<TEntity> query,
        PageRequest pageRequest,
        Expression<Func<TEntity, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
        where TEntity : Entity =>
        query.ToPagedResultAsync(pageRequest, sortField, sortOrder, page => page, cancellationToken);

    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TEntity, TResult>(
        this IQueryable<TEntity> query,
        PageRequest pageRequest,
        Expression<Func<TEntity, object>> sortField,
        SortOrder sortOrder,
        Func<IQueryable<TEntity>, IQueryable<TResult>> project,
        CancellationToken cancellationToken = default)
        where TEntity : Entity
    {
        query = sortOrder == SortOrder.Desc
            ? query.OrderByDescending(sortField).ThenBy(e => e.Id)
            : query.OrderBy(sortField).ThenBy(e => e.Id);

        query = query.AsNoTracking();

        var totalItems = await query.CountAsync(cancellationToken);

        var skip = (long)(pageRequest.Page - 1) * pageRequest.PageSize;
        var skipCount = skip > int.MaxValue ? int.MaxValue : (int)skip;

        var items = await project(query
                .Skip(skipCount)
                .Take(pageRequest.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>
        {
            Items = items,
            Page = pageRequest.Page,
            PageSize = pageRequest.PageSize,
            TotalItems = totalItems
        };
    }
}
