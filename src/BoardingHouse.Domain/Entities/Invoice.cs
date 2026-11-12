using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Invoice : BaseEntity
{
    public const int MaxCustomItems = 20;
    public const int InvoiceNumberMaxLength = 50;
    public const int DiscountReasonMaxLength = 255;
    public const int NotesMaxLength = 2000;

    public Guid OrganizationId { get; private set; }
    public Organization? Organization { get; private set; }

    public Guid LeaseId { get; private set; }
    public Lease? Lease { get; private set; }

    public string InvoiceNumber { get; private set; } = null!;
    public BillingMonth BillingMonth { get; private set; }
    public DateOnly DueDate { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal LateFeeAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal PaidAmount { get; private set; }

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Pending;
    public string? Notes { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    private readonly List<InvoiceItem> _items = [];
    private readonly List<InvoiceStatusHistory> _statusHistories = [];

    public IReadOnlyCollection<InvoiceItem> Items => _items;
    public IReadOnlyCollection<InvoiceStatusHistory> StatusHistories => _statusHistories;

    public decimal RemainingAmount => TotalAmount - PaidAmount;

    private Invoice() { }
}
