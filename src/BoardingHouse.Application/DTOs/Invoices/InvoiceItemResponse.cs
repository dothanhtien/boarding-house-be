namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceItemResponse : InvoiceLineResponse
{
    public required Guid Id { get; init; }
}
