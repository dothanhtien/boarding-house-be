using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class PropertyRepository(AppDbContext context) : Repository<Property>(context), IPropertyRepository
{
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
}
