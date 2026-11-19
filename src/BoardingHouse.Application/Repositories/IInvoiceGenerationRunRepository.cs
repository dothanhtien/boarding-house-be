using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IInvoiceGenerationRunRepository
{
    Task AddAsync(InvoiceGenerationRun run, CancellationToken cancellationToken = default);
    Task<PagedResult<InvoiceGenerationRun>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        BillingMonth? billingMonth,
        PageRequest pageRequest,
        CancellationToken cancellationToken = default);
}
