using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public interface IOnlinePaymentService
{
    Task<List<PaymentGateway>> GetMyGatewaysAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<OnlineCheckoutResponse> CreateMyCheckoutAsync(
        Guid invoiceId, CreateOnlineCheckoutRequest request, string clientIp, CancellationToken cancellationToken = default);
    Task<GatewayHttpResponse> HandleCallbackAsync(
        PaymentGateway gateway, GatewayCallback callback, CancellationToken cancellationToken = default);
    Task<OnlinePaymentReturnResponse> HandleMyReturnAsync(
        PaymentGateway gateway, OnlinePaymentReturnRequest request, CancellationToken cancellationToken = default);
}
