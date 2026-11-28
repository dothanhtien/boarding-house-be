using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public interface IOnlinePaymentService
{
    Task<List<PaymentGateway>> GetMyGatewaysAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<OnlineCheckoutResponse> CreateMyCheckoutAsync(
        Guid invoiceId, CreateOnlineCheckoutRequest request, string clientIp, CancellationToken cancellationToken = default);
}
