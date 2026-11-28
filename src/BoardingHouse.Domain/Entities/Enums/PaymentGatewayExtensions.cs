namespace BoardingHouse.Domain.Entities.Enums;

public static class PaymentGatewayExtensions
{
    public static PaymentMethod ToPaymentMethod(this PaymentGateway gateway) => gateway switch
    {
        PaymentGateway.VnPay => PaymentMethod.VnPay,
        _ => throw new ArgumentOutOfRangeException(nameof(gateway), gateway, "Unknown payment gateway")
    };
}
