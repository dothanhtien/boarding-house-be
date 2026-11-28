namespace BoardingHouse.Application.Services.PaymentGateways;

public class PaymentGatewayException(
    string message,
    bool isTimeout,
    string? requestPayload = null,
    string? responsePayload = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public bool IsTimeout { get; } = isTimeout;
    public string? RequestPayload { get; } = requestPayload;
    public string? ResponsePayload { get; } = responsePayload;
}
