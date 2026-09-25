using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class UtilityServiceRepository(AppDbContext context) : Repository<UtilityService>(context), IUtilityServiceRepository
{
    public Task<UtilityService?> GetByIdWithPropertyAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.UtilityServices
            .Include(s => s.Property)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<UtilityService?> GetByIdWithPropertyForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.UtilityServices
            .FromSql($"SELECT * FROM utility_services WHERE id = {id} FOR UPDATE")
            .Include(s => s.Property)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<UtilityService>> ListByPropertyIdAsync(
        Guid propertyId,
        UtilityType? type,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var services = Context.UtilityServices.Where(s => s.PropertyId == propertyId);

        if (type is not null)
        {
            services = services.Where(s => s.Type == type);
        }

        if (isActive is not null)
        {
            services = services.Where(s => s.IsActive == isActive);
        }

        return services.OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }
}
