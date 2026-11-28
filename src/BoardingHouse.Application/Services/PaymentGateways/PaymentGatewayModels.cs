namespace BoardingHouse.Application.Services.PaymentGateways;

public record GatewayCredentials(string MerchantCode, string Secret);

public record GatewayCheckoutRequest(
    Guid PaymentId,
    decimal Amount,
    string OrderInfo,
    string ClientIp,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    GatewayCredentials Credentials);

public record GatewayCheckout(string PaymentUrl, string RequestPayload, string? ResponsePayload);

public record GatewayCallback(IReadOnlyDictionary<string, string> Query, string? Body);

public record GatewayCallbackData(
    Guid? PaymentId,
    string? GatewayTransactionId,
    decimal? Amount,
    bool IsSuccess,
    string Payload);

public enum GatewayCallbackOutcome
{
    Received,
    AlreadyProcessed,
    PaymentNotFound,
    InvalidAmount,
    InvalidSignature,
    Error
}

public record GatewayHttpResponse(int StatusCode, string? ContentType, string? Body);
