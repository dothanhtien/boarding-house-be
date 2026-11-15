using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class InvoiceStatusHistory : Entity
{
    public Guid InvoiceId { get; private set; }
    public Invoice? Invoice { get; private set; }

    public InvoiceStatus? OldStatus { get; private set; }
    public InvoiceStatus NewStatus { get; private set; }
    public string? Reason { get; private set; }

    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private InvoiceStatusHistory() { }

    public InvoiceStatusHistory(
        Guid invoiceId, InvoiceStatus? oldStatus, InvoiceStatus newStatus, string? reason, Guid? changedBy, DateTimeOffset changedAt)
    {
        InvoiceId = invoiceId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Reason = reason;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }
}
