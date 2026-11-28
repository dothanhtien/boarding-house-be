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
    public int? CredentialsVersion { get; private set; }
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
        int credentialsVersion,
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
            CredentialsVersion = credentialsVersion,
            CreatedAt = now
        };

    public static PaymentTransaction CheckoutFailed(
        Guid paymentId,
        PaymentGateway gateway,
        decimal amount,
        string? requestPayload,
        string? responsePayload,
        string error,
        int credentialsVersion,
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
            CredentialsVersion = credentialsVersion,
            CreatedAt = now
        };

    private static string? Truncate(string? error) => error is { Length: > ErrorMaxLength } ? error[..ErrorMaxLength] : error;

    public static PaymentTransaction Callback(
        Guid? paymentId,
        PaymentGateway gateway,
        PaymentTransactionStatus status,
        string? gatewayTransactionId,
        decimal? amount,
        string webhookPayload,
        string? error,
        bool needsReview,
        DateTimeOffset now,
        PaymentTransactionType source = PaymentTransactionType.Callback)
    {
        if (status == PaymentTransactionStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A callback is either a success or a failure");
        }

        if (source == PaymentTransactionType.Checkout)
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "A callback comes from the IPN or the return page");
        }

        return new PaymentTransaction
        {
            PaymentId = paymentId,
            Gateway = gateway,
            Type = source,
            Status = status,
            GatewayTransactionId = gatewayTransactionId,
            Amount = amount,
            WebhookPayload = webhookPayload,
            Error = Truncate(error),
            NeedsReview = needsReview,
            CreatedAt = now
        };
    }
}
