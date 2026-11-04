using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Common;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Infrastructure.Persistence.Listing;

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

    public static Task<PagedResult<TResult>> ToPagedResultAsync<TEntity, TResult>(
        this IQueryable<TEntity> query,
        PageRequest pageRequest,
        Expression<Func<TEntity, object>> sortField,
        SortOrder sortOrder,
        Func<IQueryable<TEntity>, IQueryable<TResult>> project,
        CancellationToken cancellationToken = default)
        where TEntity : Entity =>
        query.ToPagedResultAsync(pageRequest, sortField, sortOrder, e => e.Id, project, cancellationToken);

    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TKey, TResult>(
        this IQueryable<TSource> query,
        PageRequest pageRequest,
        Expression<Func<TSource, object>> sortField,
        SortOrder sortOrder,
        Expression<Func<TSource, TKey>> tiebreak,
        Func<IQueryable<TSource>, IQueryable<TResult>> project,
        CancellationToken cancellationToken = default)
        where TSource : class
    {
        var ordered = sortOrder == SortOrder.Desc
            ? query.OrderByDescending(sortField)
            : query.OrderBy(sortField);
        var sorted = ordered.ThenBy(tiebreak).AsNoTracking();

        var totalItems = await sorted.CountAsync(cancellationToken);

        var skip = (long)(pageRequest.Page - 1) * pageRequest.PageSize;
        var skipCount = skip > int.MaxValue ? int.MaxValue : (int)skip;

        var items = await project(sorted
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
