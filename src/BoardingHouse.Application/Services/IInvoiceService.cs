using BoardingHouse.Application.DTOs.Invoices;

namespace BoardingHouse.Application.Services;

public interface IInvoiceService
{
    Task<InvoicePreviewResponse> PreviewAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
}
