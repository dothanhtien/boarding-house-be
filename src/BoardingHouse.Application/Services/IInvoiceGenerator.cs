using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public interface IInvoiceGenerator
{
    Task<InvoiceCalculation> CalculateAsync(Lease lease, InvoiceDraft draft, CancellationToken cancellationToken = default);

    Task<InvoiceGenerationResult> GenerateAsync(InvoiceDraft draft, Guid? createdBy, CancellationToken cancellationToken = default);

    Task<InvoiceGenerationRun> GenerateAllAsync(
        Guid organizationId,
        Guid? propertyId,
        BillingMonth billingMonth,
        DateOnly today,
        IReadOnlyList<Lease> leases,
        Guid? triggeredBy,
        CancellationToken cancellationToken = default);
}
