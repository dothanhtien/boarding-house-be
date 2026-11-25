using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Application.Queries;

namespace BoardingHouse.Infrastructure.Queries;

public class TenantPortalQueries(AppDbContext context) : ITenantPortalQueries
{
    private IQueryable<Stay> StaysOf(Guid userId) =>
        context.LeaseTenants
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId
                && t.DeletedAt == null
                && t.Lease!.DeletedAt == null
                && t.Lease.Status != LeaseStatus.Draft)
            .Select(t => new Stay
            {
                LeaseId = t.LeaseId,
                LeaseStatus = t.Lease!.Status,
                From = t.JoinedDate ?? t.Lease.StartDate,
                To = t.LeftDate
            });

    private sealed class Stay
    {
        public Guid LeaseId { get; init; }
        public LeaseStatus LeaseStatus { get; init; }
        public DateOnly From { get; init; }
        public DateOnly? To { get; init; }
    }

    public async Task<TenancyResponse> GetTenancyAsync(Guid userId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var stays = StaysOf(userId);

        var total = await stays.Select(s => s.LeaseId).Distinct().CountAsync(cancellationToken);
        var active = total == 0
            ? 0
            : await stays
                .Where(s => s.LeaseStatus == LeaseStatus.Active && (s.To == null || s.To >= today))
                .Select(s => s.LeaseId)
                .Distinct()
                .CountAsync(cancellationToken);

        return new TenancyResponse { ActiveLeaseCount = active, TotalLeaseCount = total };
    }
}
