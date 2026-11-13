namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceDetailResponse : InvoiceResponse
{
    public required List<InvoiceItemResponse> Items { get; init; }
}
