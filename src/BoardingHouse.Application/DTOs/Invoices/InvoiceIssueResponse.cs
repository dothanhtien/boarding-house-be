using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceIssueResponse
{
    public required InvoiceIssueCode Code { get; init; }
    public required string Message { get; init; }
    public Guid? ServiceId { get; init; }
}
