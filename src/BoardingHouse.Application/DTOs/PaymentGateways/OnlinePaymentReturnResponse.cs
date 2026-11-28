using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.PaymentGateways;

public record OnlinePaymentReturnResponse
{
    public required Guid PaymentId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required GatewayCallbackOutcome Outcome { get; init; }
    public required PaymentStatus PaymentStatus { get; init; }
}
