using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceStatusHistoryResponse
{
    public required Guid Id { get; init; }
    public InvoiceStatus? OldStatus { get; init; }
    public required InvoiceStatus NewStatus { get; init; }
    public string? Reason { get; init; }
    public Guid? ChangedBy { get; init; }
    public required DateTimeOffset ChangedAt { get; init; }
}
