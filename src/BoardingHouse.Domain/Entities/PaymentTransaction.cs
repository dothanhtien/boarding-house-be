using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class PaymentTransaction : Entity
{
    public const int GatewayTransactionIdMaxLength = 255;
    public const int ErrorMaxLength = 500;

    public Guid? PaymentId { get; private set; }
    public PaymentGateway Gateway { get; private set; }
    public PaymentTransactionType Type { get; private set; }
    public PaymentTransactionStatus Status { get; private set; }
    public string? GatewayTransactionId { get; private set; }
    public decimal? Amount { get; private set; }
    public string? PaymentUrl { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? RequestPayload { get; private set; }
    public string? ResponsePayload { get; private set; }
    public string? WebhookPayload { get; private set; }
    public string? Error { get; private set; }

    public bool NeedsReview { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private PaymentTransaction() { }

    public static PaymentTransaction Checkout(
        Guid paymentId,
        PaymentGateway gateway,
        decimal amount,
        string paymentUrl,
        DateTimeOffset expiresAt,
        string requestPayload,
        string? responsePayload,
        DateTimeOffset now) => new()
        {
            PaymentId = paymentId,
            Gateway = gateway,
            Type = PaymentTransactionType.Checkout,
            Status = PaymentTransactionStatus.Pending,
            Amount = amount,
            PaymentUrl = paymentUrl,
            ExpiresAt = expiresAt,
            RequestPayload = requestPayload,
            ResponsePayload = responsePayload,
            CreatedAt = now
        };

    public static PaymentTransaction CheckoutFailed(
        Guid paymentId,
        PaymentGateway gateway,
        decimal amount,
        string? requestPayload,
        string? responsePayload,
        string error,
        DateTimeOffset now) => new()
        {
            PaymentId = paymentId,
            Gateway = gateway,
            Type = PaymentTransactionType.Checkout,
            Status = PaymentTransactionStatus.Failed,
            Amount = amount,
            RequestPayload = requestPayload,
            ResponsePayload = responsePayload,
            Error = Truncate(error),
            CreatedAt = now
        };

    private static string? Truncate(string? error) => error is { Length: > ErrorMaxLength } ? error[..ErrorMaxLength] : error;
}
