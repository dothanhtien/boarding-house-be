using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.PaymentGateways;

public record CreateOnlineCheckoutRequest
{
    public PaymentGateway? Gateway { get; init; }
}
