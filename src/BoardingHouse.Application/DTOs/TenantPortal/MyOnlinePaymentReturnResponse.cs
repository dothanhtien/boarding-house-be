using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyOnlinePaymentReturnResponse
{
    public required Guid PaymentId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required GatewayCallbackOutcome Outcome { get; init; }
    public required PaymentStatus PaymentStatus { get; init; }
}
