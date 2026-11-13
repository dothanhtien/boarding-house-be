using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Domain.Entities;

public class InvoiceItem : Entity
{
    public const int DescriptionMaxLength = 255;

    public Guid InvoiceId { get; private set; }
    public Invoice? Invoice { get; private set; }

    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount { get; private set; }
    public int SortOrder { get; private set; }

    public InvoiceItemSourceType SourceType { get; private set; }
    public Guid? SourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsCustom => SourceType == InvoiceItemSourceType.Custom;

    private InvoiceItem() { }

    public InvoiceItem(Guid invoiceId, InvoiceLine line, int sortOrder, DateTimeOffset createdAt)
    {
        if (line.SourceType == InvoiceItemSourceType.Custom)
        {
            EnsureCustomValid(line.Description, line.Quantity, line.UnitPrice);
        }

        InvoiceId = invoiceId;
        Description = line.Description;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        Amount = line.Amount;
        SortOrder = sortOrder;
        SourceType = line.SourceType;
        SourceId = line.SourceId;
        CreatedAt = createdAt;
    }

    public static void EnsureCustomValid(string? description, decimal? quantity, decimal? unitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("Description is required");
        }

        if (description.Trim().Length > DescriptionMaxLength)
        {
            throw new DomainException($"Description must not exceed {DescriptionMaxLength} characters");
        }

        if (quantity is not > 0)
        {
            throw new DomainException("Quantity must be greater than 0");
        }

        if (unitPrice is null or 0)
        {
            throw new DomainException(unitPrice is null ? "UnitPrice is required" : "UnitPrice must not be 0");
        }
    }
}
