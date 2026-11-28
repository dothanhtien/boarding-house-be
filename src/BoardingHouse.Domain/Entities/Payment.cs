using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Payment : AuditableEntity
{
    public const int PayerNameOverrideMaxLength = 100;
    public const int TransactionReferenceMaxLength = 100;
    public const int NoteMaxLength = 2000;
    public const int VoidReasonMaxLength = 1000;
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;
    public const string AmountPrecisionMessage = "Amount must not exceed 18 digits in total, with at most 2 decimal places";
    public const string InvoiceCancelledReason = "Invoice cancelled";
    public const string GatewayRequestFailedReason = "Gateway request failed";
    public const string GatewayFailedReason = "Gateway payment failed";
    public const string GatewayNotAppliedReason = "Paid through the gateway but could not be applied to the invoice";

    public Guid? InvoiceId { get; private set; }
    public Invoice? Invoice { get; private set; }
    public Guid? DepositReturnId { get; private set; }

    public Guid? PayerUserId { get; private set; }
    public User? PayerUser { get; private set; }
    public string? PayerNameOverride { get; private set; }

    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string? TransactionReference { get; private set; }

    public DateTimeOffset PaymentDate { get; private set; }

    public Guid? ConfirmedBy { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? VoidedBy { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public string? Note { get; private set; }

    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan GatewayCheckoutLifetime = TimeSpan.FromMinutes(15);

    private Payment() { }

    public static Payment ForInvoice(Guid invoiceId, PaymentDetails details, Guid createdBy, DateTimeOffset now)
    {
        EnsureDetailsValid(details, now);

        return new Payment
        {
            InvoiceId = invoiceId,
            PayerUserId = details.PayerUserId,
            PayerNameOverride = TrimToNull(details.PayerNameOverride),
            Amount = details.Amount,
            Method = details.Method,
            TransactionReference = TrimToNull(details.TransactionReference),
            PaymentDate = details.PaymentDate,
            Note = string.IsNullOrWhiteSpace(details.Note) ? null : details.Note,
            CreatedBy = createdBy
        };
    }

    public void EnsureCanBeConfirmed()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw DomainException.Conflict("Only pending payments can be confirmed");
        }
    }

    public void Confirm(Guid? confirmedBy, DateTimeOffset now, string? transactionReference = null)
    {
        EnsureCanBeConfirmed();

        if (transactionReference?.Trim().Length > TransactionReferenceMaxLength)
        {
            throw new DomainException($"TransactionReference must not exceed {TransactionReferenceMaxLength} characters");
        }

        Status = PaymentStatus.Confirmed;
        ConfirmedBy = confirmedBy;
        ConfirmedAt = now;

        if (!string.IsNullOrWhiteSpace(transactionReference))
        {
            TransactionReference = transactionReference.Trim();
        }
    }

    public void Void(string reason, Guid? voidedBy, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Voided)
        {
            throw DomainException.Conflict("The payment is already voided");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Reason is required");
        }

        if (reason.Trim().Length > VoidReasonMaxLength)
        {
            throw new DomainException($"Reason must not exceed {VoidReasonMaxLength} characters");
        }

        Status = PaymentStatus.Voided;
        VoidedBy = voidedBy;
        VoidedAt = now;
        VoidReason = reason.Trim();
    }

    public static bool IsGatewayMethod(PaymentMethod method) => method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer);

    private static void EnsureDetailsValid(PaymentDetails details, DateTimeOffset now)
    {
        if (details.Amount <= 0)
        {
            throw new DomainException("Amount must be greater than 0");
        }

        if (details.Amount > MaxAmount || decimal.Round(details.Amount, 2) != details.Amount)
        {
            throw new DomainException(AmountPrecisionMessage);
        }

        if (details.PaymentDate > now + MaxClockSkew)
        {
            throw new DomainException("PaymentDate must not be in the future");
        }

        if (details.PayerUserId is not null && !string.IsNullOrWhiteSpace(details.PayerNameOverride))
        {
            throw new DomainException("Send either PayerUserId or PayerNameOverride, not both");
        }

        if (details.PayerNameOverride?.Trim().Length > PayerNameOverrideMaxLength)
        {
            throw new DomainException($"PayerNameOverride must not exceed {PayerNameOverrideMaxLength} characters");
        }

        if (details.TransactionReference?.Trim().Length > TransactionReferenceMaxLength)
        {
            throw new DomainException($"TransactionReference must not exceed {TransactionReferenceMaxLength} characters");
        }

        if (details.Note?.Length > NoteMaxLength)
        {
            throw new DomainException($"Note must not exceed {NoteMaxLength} characters");
        }
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
