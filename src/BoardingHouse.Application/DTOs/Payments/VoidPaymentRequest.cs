namespace BoardingHouse.Application.DTOs.Payments;

public record VoidPaymentRequest
{
    public required string Reason { get; init; }
}
