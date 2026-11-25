using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyPaymentResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required string PropertyName { get; init; }
    public required string RoomNumber { get; init; }
    public required string LeaseCode { get; init; }
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public string? PayerName { get; init; }
    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public required PaymentStatus Status { get; init; }
    public string? TransactionReference { get; init; }
    public required DateTimeOffset PaymentDate { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
    public DateTimeOffset? VoidedAt { get; init; }
}
