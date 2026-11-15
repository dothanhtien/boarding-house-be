using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Invoicing;

public sealed record InvoiceLine(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    InvoiceItemSourceType SourceType,
    Guid? SourceId)
{
    public static InvoiceLine Custom(string description, decimal quantity, decimal unitPrice) =>
        new(description.Trim(), quantity, unitPrice, Money.Round(quantity * unitPrice), InvoiceItemSourceType.Custom, null);
}
