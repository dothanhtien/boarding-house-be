using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Payments;

namespace BoardingHouse.Application.Services;

public interface IPaymentService
{
    Task<PagedResult<PaymentResponse>> GetAllAsync(PaymentListQuery query, CancellationToken cancellationToken = default);
    Task<List<PaymentResponse>> GetByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<PaymentResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentResponse> RecordAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentResponse> VoidAsync(Guid id, VoidPaymentRequest request, CancellationToken cancellationToken = default);
}
