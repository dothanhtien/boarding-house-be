namespace BoardingHouse.Application.DTOs.Invoices;

public record CreateInvoiceRequest
{
    public Guid LeaseId { get; init; }
    public DateOnly? BillingMonth { get; init; }
    public List<CreateInvoiceCustomItemRequest>? CustomItems { get; init; }
    public decimal DiscountAmount { get; init; }
    public string? DiscountReason { get; init; }
    public string? Notes { get; init; }
}
