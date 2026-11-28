namespace BoardingHouse.Application.DTOs.PaymentGateways;

public record OnlinePaymentReturnRequest
{
    public Dictionary<string, string>? Params { get; init; }
}
