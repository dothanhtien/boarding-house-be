using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Invoices;

namespace BoardingHouse.Application.Services;

public interface IInvoiceGenerationService
{
    Task<InvoiceGenerationRunResponse> GenerateForPropertyAsync(
        Guid propertyId, GenerateInvoicesRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<InvoiceGenerationRunResponse>> GetRunsAsync(
        InvoiceGenerationRunListQuery query, CancellationToken cancellationToken = default);
}
