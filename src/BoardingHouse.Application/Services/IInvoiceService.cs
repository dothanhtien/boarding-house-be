using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Invoices;

namespace BoardingHouse.Application.Services;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceResponse>> GetAllAsync(InvoiceListQuery query, CancellationToken cancellationToken = default);
    Task<InvoiceDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<InvoiceDetailResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<InvoicePreviewResponse> PreviewAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
}
