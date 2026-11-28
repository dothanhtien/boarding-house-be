using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public interface IOnlinePaymentService
{
    Task<List<MyOnlinePaymentGatewayResponse>> GetMyGatewaysAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<MyOnlineCheckoutResponse> CreateMyCheckoutAsync(
        Guid invoiceId, CreateOnlineCheckoutRequest request, string clientIp, CancellationToken cancellationToken = default);
    Task<GatewayHttpResponse> HandleCallbackAsync(
        PaymentGateway gateway, GatewayCallback callback, CancellationToken cancellationToken = default);
    Task<MyOnlinePaymentReturnResponse> HandleMyReturnAsync(
        PaymentGateway gateway, OnlinePaymentReturnRequest request, CancellationToken cancellationToken = default);
}
