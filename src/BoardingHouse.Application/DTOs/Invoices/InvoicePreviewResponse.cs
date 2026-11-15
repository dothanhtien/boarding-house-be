namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoicePreviewResponse
{
    public required Guid OrganizationId { get; init; }
    public required Guid PropertyId { get; init; }
    public required string PropertyName { get; init; }
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public required Guid LeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required DateOnly BillingMonth { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal VatRate { get; init; }
    public required decimal VatAmount { get; init; }
    public required decimal DiscountAmount { get; init; }
    public string? DiscountReason { get; init; }
    public required decimal TotalAmount { get; init; }
    public required List<InvoiceLineResponse> Items { get; init; }
    public required List<InvoiceIssueResponse> Errors { get; init; }
    public required List<InvoiceIssueResponse> Warnings { get; init; }
    public required bool CanCreate { get; init; }
}
