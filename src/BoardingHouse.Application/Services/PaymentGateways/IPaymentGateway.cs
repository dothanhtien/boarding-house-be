using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services.PaymentGateways;

public interface IPaymentGateway
{
    PaymentGateway Gateway { get; }

    Task<GatewayCheckout> CreateCheckoutAsync(GatewayCheckoutRequest request, CancellationToken cancellationToken = default);
    GatewayCallbackData ParseCallback(GatewayCallback callback);
    bool VerifyCallback(GatewayCallback callback, GatewayCredentials credentials);
    GatewayHttpResponse BuildCallbackResponse(GatewayCallbackOutcome outcome);
}
