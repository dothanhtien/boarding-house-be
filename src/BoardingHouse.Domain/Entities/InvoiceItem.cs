using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

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
}
