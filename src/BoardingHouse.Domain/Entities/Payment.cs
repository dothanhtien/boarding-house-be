using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Payment : AuditableEntity
{
    public const int PayerNameOverrideMaxLength = 100;
    public const int TransactionReferenceMaxLength = 100;
    public const int NoteMaxLength = 2000;
    public const int VoidReasonMaxLength = 1000;

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

    private Payment() { }
}
