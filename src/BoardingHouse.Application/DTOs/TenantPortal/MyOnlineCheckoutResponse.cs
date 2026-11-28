using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyOnlineCheckoutResponse
{
    public required Guid PaymentId { get; init; }
    public required PaymentGateway Gateway { get; init; }
    public required decimal Amount { get; init; }
    public required string PaymentUrl { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
