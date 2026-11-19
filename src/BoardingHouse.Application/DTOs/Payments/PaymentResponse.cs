using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Payments;

public record PaymentResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid PropertyId { get; init; }
    public required string PropertyName { get; init; }
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public required Guid LeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public Guid? PayerUserId { get; init; }
    public string? PayerNameOverride { get; init; }

    public string? PayerName { get; init; }

    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public required PaymentStatus Status { get; init; }
    public string? TransactionReference { get; init; }
    public required DateTimeOffset PaymentDate { get; init; }
    public Guid? ConfirmedBy { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
    public Guid? VoidedBy { get; init; }
    public DateTimeOffset? VoidedAt { get; init; }
    public string? VoidReason { get; init; }
    public string? Note { get; init; }
    public required Guid CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
