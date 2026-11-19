using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class InvoiceGenerationRunRepository(AppDbContext context) : IInvoiceGenerationRunRepository
{
    public Task AddAsync(InvoiceGenerationRun run, CancellationToken cancellationToken = default) =>
        context.InvoiceGenerationRuns.AddAsync(run, cancellationToken).AsTask();

    public Task<PagedResult<InvoiceGenerationRun>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        BillingMonth? billingMonth,
        PageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        var runs = context.InvoiceGenerationRuns.AsQueryable();

        if (organizationId is not null)
        {
            runs = runs.Where(r => r.OrganizationId == organizationId);
        }
        else if (allowedOrganizationIds is not null)
        {
            runs = runs.Where(r => allowedOrganizationIds.Contains(r.OrganizationId));
        }

        if (propertyId is not null)
        {
            runs = runs.Where(r => r.PropertyId == propertyId);
        }

        if (billingMonth is { } month)
        {
            runs = runs.Where(r => r.BillingMonth == month);
        }

        return runs.ToPagedResultAsync(pageRequest, r => r.StartedAt, SortOrder.Desc, cancellationToken);
    }
}
