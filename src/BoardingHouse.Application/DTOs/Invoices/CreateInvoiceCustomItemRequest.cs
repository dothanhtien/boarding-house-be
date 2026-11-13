namespace BoardingHouse.Application.DTOs.Invoices;

public record CreateInvoiceCustomItemRequest
{
    public string? Description { get; init; }
    public decimal Quantity { get; init; } = 1;
    public decimal? UnitPrice { get; init; }
}
