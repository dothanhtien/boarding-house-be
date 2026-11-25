using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyInvoiceResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required string PropertyName { get; init; }
    public required string RoomNumber { get; init; }
    public required Guid LeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required string InvoiceNumber { get; init; }
    public required DateOnly BillingMonth { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal VatRate { get; init; }
    public required decimal VatAmount { get; init; }
    public required decimal LateFeeAmount { get; init; }
    public required decimal DiscountAmount { get; init; }
    public string? DiscountReason { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal PaidAmount { get; init; }
    public required decimal RemainingAmount { get; init; }
    public required InvoiceStatus Status { get; init; }
    public DateTimeOffset? SentAt { get; init; }
}
