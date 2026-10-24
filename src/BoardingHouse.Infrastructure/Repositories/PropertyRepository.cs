using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class PropertyRepository(AppDbContext context) : Repository<Property>(context), IPropertyRepository
{
    public Task<Property?> GetByIdForShareAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Properties.FromSql($"SELECT * FROM properties WHERE id = {id} FOR SHARE").FirstOrDefaultAsync(cancellationToken);

    public Task<Property?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Properties.FromSql($"SELECT * FROM properties WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<Property>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        bool? isActive,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Property, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var properties = Context.Properties.AsQueryable();

        if (organizationId is not null)
        {
            properties = properties.Where(o => o.OrganizationId == organizationId);
        }
        else if (allowedOrganizationIds is not null)
        {
            properties = properties.Where(o => allowedOrganizationIds.Contains(o.OrganizationId));
        }

        if (isActive is not null)
        {
            properties = properties.Where(o => o.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            properties = properties.Where(o => EF.Functions.ILike(o.Name, pattern, LikePattern.EscapeCharacter));
        }

        return await properties.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }

    public Task<List<(Guid Id, string Name)>> ListActiveByOrganizationIdAsync(
        Guid organizationId, CancellationToken cancellationToken = default) =>
        Context.Properties
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ValueTuple<Guid, string>(p.Id, p.Name))
            .ToListAsync(cancellationToken);
}
