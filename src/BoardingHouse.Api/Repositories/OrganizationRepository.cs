using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class OrganizationRepository(AppDbContext context)
    : Repository<Organization>(context), IOrganizationRepository
{
    public Task<Organization?> GetByIdWithMembersAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Organizations
            .AsNoTracking()
            .Include(o => o.Members.OrderBy(m => m.CreatedAt))
                .ThenInclude(m => m.User)
            .Include(o => o.Members.OrderBy(m => m.CreatedAt))
                .ThenInclude(m => m.Role)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<PagedResult<Organization>> SearchAsync(
        bool? isActive,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Organization, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        var organizations = Context.Organizations.AsQueryable();

        if (isActive is not null)
        {
            organizations = organizations.Where(o => o.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            organizations = organizations.Where(o => EF.Functions.ILike(o.Name, pattern, LikePattern.EscapeCharacter));
        }

        return await organizations.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }
}
