using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Payments;

public record RecordPaymentRequest
{
    public decimal Amount { get; init; }
    public PaymentMethod? Method { get; init; }
    public DateTimeOffset? PaymentDate { get; init; }

    public Guid? PayerUserId { get; init; }
    public string? PayerNameOverride { get; init; }

    public string? TransactionReference { get; init; }
    public string? Note { get; init; }

    public bool Confirm { get; init; }
}
