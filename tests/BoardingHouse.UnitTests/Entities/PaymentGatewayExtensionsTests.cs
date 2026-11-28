using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.UnitTests.Entities;

public class PaymentGatewayExtensionsTests
{
    public static TheoryData<PaymentGateway> Gateways() => new(Enum.GetValues<PaymentGateway>());

    [Fact]
    public void ToPaymentMethod_VnPay_IsVnPay()
    {
        Assert.Equal(PaymentMethod.VnPay, PaymentGateway.VnPay.ToPaymentMethod());
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public void ToPaymentMethod_EveryGateway_IsMapped(PaymentGateway gateway)
    {
        var exception = Record.Exception(() => gateway.ToPaymentMethod());

        Assert.Null(exception);
    }

    [Fact]
    public void ToPaymentMethod_UnknownGateway_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((PaymentGateway)999).ToPaymentMethod());
    }

    // payment_transactions.gateway and payments.method must hold the same string for the same gateway
    [Theory]
    [MemberData(nameof(Gateways))]
    public void DbValue_IsTheSameAsItsPaymentMethod(PaymentGateway gateway)
    {
        Assert.Equal(
            PaymentMethodDbValueConverter.Instance.ConvertToProvider(gateway.ToPaymentMethod()),
            PaymentGatewayDbValueConverter.Instance.ConvertToProvider(gateway));
    }
}
