using System.Globalization;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;

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

    public bool IsEditable => PaidAmount == 0 && Status is InvoiceStatus.Pending or InvoiceStatus.Overdue;

    public InvoiceDetails Details => new(DiscountAmount, DiscountReason, Notes, DueDate);

    private Invoice() { }

    public static Invoice Create(
        InvoiceCalculation calculation,
        Guid organizationId,
        Guid leaseId,
        string invoiceNumber,
        string? discountReason,
        string? notes,
        DateTimeOffset now,
        Guid createdBy)
    {
        if (!calculation.CanCreate)
        {
            throw new InvalidOperationException("An invoice cannot be created from a calculation with errors");
        }

        var details = new InvoiceDetails(calculation.DiscountAmount, discountReason, notes, calculation.DueDate);
        EnsureDetailsValid(details, calculation.BillingMonth);

        var invoice = new Invoice
        {
            OrganizationId = organizationId,
            LeaseId = leaseId,
            InvoiceNumber = invoiceNumber,
            BillingMonth = calculation.BillingMonth,
            VatRate = calculation.VatRate,
            CreatedBy = createdBy
        };

        invoice.ApplyDetails(details);
        invoice._items.AddRange(calculation.Lines.Select((line, index) => new InvoiceItem(invoice.Id, line, index, now)));
        invoice.ApplyTotals(invoice.ComputeTotals(invoice._items.Select(i => (i.SourceType, i.Amount)), details.DiscountAmount));
        invoice._statusHistories.Add(
            new InvoiceStatusHistory(invoice.Id, oldStatus: null, invoice.Status, reason: null, createdBy, now));

        return invoice;
    }

    public void Update(InvoiceDetails details, IReadOnlyList<InvoiceCustomItemChange> customItemChanges, DateTimeOffset now)
    {
        if (!IsEditable)
        {
            throw DomainException.Conflict("Only unpaid pending or overdue invoices can be edited");
        }

        EnsureDetailsValid(details, BillingMonth);

        var deletedIds = new HashSet<Guid>();
        var edits = new List<(InvoiceItem Item, string Description, decimal Quantity, decimal UnitPrice)>();

        foreach (var change in customItemChanges.Where(c => c.ItemId is not null))
        {
            var item = _items.FirstOrDefault(i => i.Id == change.ItemId)
                ?? throw DomainException.NotFound($"Invoice item '{change.ItemId}' not found");

            if (!item.IsCustom)
            {
                throw DomainException.Conflict(
                    "Only custom items can be changed — cancel the invoice and create it again to recompute the other items");
            }

            if (change.IsDeleted)
            {
                deletedIds.Add(item.Id);
                continue;
            }

            var description = change.Description ?? item.Description;
            var quantity = change.Quantity ?? item.Quantity;
            var unitPrice = change.UnitPrice ?? item.UnitPrice;
            InvoiceItem.EnsureCustomValid(description, quantity, unitPrice);
            edits.Add((item, description, quantity, unitPrice));
        }

        var nextSortOrder = _items.Count == 0 ? 0 : _items.Max(i => i.SortOrder) + 1;
        var added = customItemChanges
            .Where(c => c.ItemId is null)
            .Select((c, index) =>
            {
                InvoiceItem.EnsureCustomValid(c.Description, c.Quantity, c.UnitPrice);
                return new InvoiceItem(
                    Id, InvoiceLine.Custom(c.Description!, c.Quantity!.Value, c.UnitPrice!.Value), nextSortOrder + index, now);
            })
            .ToList();

        var remaining = _items.Where(i => !deletedIds.Contains(i.Id)).ToList();

        if (remaining.Count(i => i.IsCustom) + added.Count > MaxCustomItems)
        {
            throw new DomainException($"At most {MaxCustomItems} custom items are allowed");
        }

        if (remaining.Count + added.Count == 0)
        {
            throw new DomainException("An invoice must keep at least one item");
        }

        var finalLines = remaining
            .Select(i => edits.FirstOrDefault(e => e.Item == i) is { Item: not null } edit
                ? (i.SourceType, InvoiceLine.Custom(edit.Description, edit.Quantity, edit.UnitPrice).Amount)
                : (i.SourceType, i.Amount))
            .Concat(added.Select(i => (i.SourceType, i.Amount)));
        var totals = ComputeTotals(finalLines, details.DiscountAmount);

        if (totals.TotalAmount < 0)
        {
            throw new DomainException("The invoice total must not be negative");
        }

        ApplyDetails(details);
        _items.RemoveAll(i => deletedIds.Contains(i.Id));
        foreach (var (item, description, quantity, unitPrice) in edits)
        {
            item.ChangeCustom(description, quantity, unitPrice);
        }
        _items.AddRange(added);
        ApplyTotals(totals);
    }

    public static string FormatNumber(BillingMonth month, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"INV-{month.FirstDay:yyyyMM}-{number:D4}");

    private static void EnsureDetailsValid(InvoiceDetails details, BillingMonth billingMonth)
    {
        if (details.DiscountAmount < 0)
        {
            throw new DomainException("DiscountAmount must not be negative");
        }

        if (details.DiscountAmount > 0 && string.IsNullOrWhiteSpace(details.DiscountReason))
        {
            throw new DomainException("DiscountReason is required when DiscountAmount is greater than 0");
        }

        if (details.DiscountReason?.Trim().Length > DiscountReasonMaxLength)
        {
            throw new DomainException($"DiscountReason must not exceed {DiscountReasonMaxLength} characters");
        }

        if (details.Notes?.Length > NotesMaxLength)
        {
            throw new DomainException($"Notes must not exceed {NotesMaxLength} characters");
        }

        if (details.DueDate < billingMonth.FirstDay)
        {
            throw new DomainException("DueDate must not be before the billing month");
        }
    }

    private void ApplyDetails(InvoiceDetails details)
    {
        DiscountAmount = details.DiscountAmount;
        DiscountReason = details.DiscountAmount > 0 ? details.DiscountReason!.Trim() : null;
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes;
        DueDate = details.DueDate;
    }

    private InvoiceTotals ComputeTotals(
        IEnumerable<(InvoiceItemSourceType SourceType, decimal Amount)> lines, decimal discountAmount) =>
        InvoiceTotals.Compute(lines, VatRate, LateFeeAmount, discountAmount);

    private void ApplyTotals(InvoiceTotals totals)
    {
        Subtotal = totals.Subtotal;
        VatAmount = totals.VatAmount;
        TotalAmount = totals.TotalAmount;
    }
}
