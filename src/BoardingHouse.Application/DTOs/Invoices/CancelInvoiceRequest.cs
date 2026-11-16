namespace BoardingHouse.Application.DTOs.Invoices;

public record CancelInvoiceRequest
{
    public required string Reason { get; init; }
}
