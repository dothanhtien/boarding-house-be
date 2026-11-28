namespace BoardingHouse.Application.DTOs.PaymentGateways;

public record UpsertOrganizationPaymentGatewayRequest
{
    public string MerchantCode { get; init; } = "";
    public string? Secret { get; init; }
    public bool? IsActive { get; init; }
}
