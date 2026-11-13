using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceLineResponse
{
    public required string Description { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Amount { get; init; }
    public required int SortOrder { get; init; }
    public required InvoiceItemSourceType SourceType { get; init; }
    public Guid? SourceId { get; init; }
}
