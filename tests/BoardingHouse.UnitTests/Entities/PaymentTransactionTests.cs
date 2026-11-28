using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class PaymentTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 3, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Checkout_IsAPendingCheckoutWithTheLink()
    {
        var paymentId = Guid.NewGuid();

        var transaction = PaymentTransaction.Checkout(
            paymentId, PaymentGateway.VnPay, 3_000_000m, "https://pay.test/x", Now.AddMinutes(15), "{}", null, Now);

        Assert.Equal(paymentId, transaction.PaymentId);
        Assert.Equal(PaymentTransactionType.Checkout, transaction.Type);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
        Assert.Equal(3_000_000m, transaction.Amount);
        Assert.Equal("https://pay.test/x", transaction.PaymentUrl);
        Assert.Equal(Now.AddMinutes(15), transaction.ExpiresAt);
        Assert.Equal(Now, transaction.CreatedAt);
        Assert.False(transaction.NeedsReview);
    }

    [Fact]
    public void CheckoutFailed_HasNoLinkAndCutsALongError()
    {
        var transaction = PaymentTransaction.CheckoutFailed(
            Guid.NewGuid(), PaymentGateway.VnPay, 3_000_000m, null, null, new string('e', 600), Now);

        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        Assert.Null(transaction.PaymentUrl);
        Assert.Null(transaction.ExpiresAt);
        Assert.Equal(PaymentTransaction.ErrorMaxLength, transaction.Error!.Length);
    }
}
