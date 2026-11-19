using BoardingHouse.Application.DTOs.Payments;

namespace BoardingHouse.Application.Services;

public interface IPaymentService
{
    Task<PaymentResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentResponse> RecordAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken = default);
}
