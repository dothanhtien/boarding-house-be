using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.PaymentGateways;

public record OrganizationPaymentGatewayResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required PaymentGateway Gateway { get; init; }
    public required string MerchantCode { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
