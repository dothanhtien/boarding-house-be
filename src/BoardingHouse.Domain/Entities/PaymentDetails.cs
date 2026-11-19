using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public record PaymentDetails(
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset PaymentDate,
    Guid? PayerUserId,
    string? PayerNameOverride,
    string? TransactionReference,
    string? Note);
