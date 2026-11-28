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
            paymentId, PaymentGateway.VnPay, 3_000_000m, "https://pay.test/x", Now.AddMinutes(15), "{}", null,
            credentialsVersion: 2, Now);

        Assert.Equal(paymentId, transaction.PaymentId);
        Assert.Equal(PaymentTransactionType.Checkout, transaction.Type);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
        Assert.Equal(3_000_000m, transaction.Amount);
        Assert.Equal("https://pay.test/x", transaction.PaymentUrl);
        Assert.Equal(Now.AddMinutes(15), transaction.ExpiresAt);
        Assert.Equal(2, transaction.CredentialsVersion);
        Assert.Equal(Now, transaction.CreatedAt);
        Assert.False(transaction.NeedsReview);
    }

    [Fact]
    public void CheckoutFailed_HasNoLinkAndCutsALongError()
    {
        var transaction = PaymentTransaction.CheckoutFailed(
            Guid.NewGuid(), PaymentGateway.VnPay, 3_000_000m, null, null, new string('e', 600), credentialsVersion: 2, Now);

        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        Assert.Null(transaction.PaymentUrl);
        Assert.Null(transaction.ExpiresAt);
        Assert.Equal(PaymentTransaction.ErrorMaxLength, transaction.Error!.Length);
        Assert.Equal(2, transaction.CredentialsVersion);
    }

    [Fact]
    public void Callback_KeepsWhatTheGatewaySent()
    {
        var paymentId = Guid.NewGuid();

        var transaction = PaymentTransaction.Callback(
            paymentId, PaymentGateway.VnPay, PaymentTransactionStatus.Success, "14123456", 3_000_000m,
            """{"vnp_SecureHash":"***"}""", error: null, needsReview: false, Now);

        Assert.Equal((PaymentTransactionType.Callback, PaymentTransactionStatus.Success), (transaction.Type, transaction.Status));
        Assert.Equal(("14123456", 3_000_000m), (transaction.GatewayTransactionId, transaction.Amount));
        Assert.Equal("""{"vnp_SecureHash":"***"}""", transaction.WebhookPayload);
        Assert.Null(transaction.PaymentUrl);
    }

    [Fact]
    public void Callback_Pending_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentTransaction.Callback(
            null, PaymentGateway.VnPay, PaymentTransactionStatus.Pending, null, null, "{}", null, false, Now));

    [Fact]
    public void Callback_FromTheReturnPage_IsRecordedAsAReturn()
    {
        var transaction = PaymentTransaction.Callback(
            Guid.NewGuid(), PaymentGateway.VnPay, PaymentTransactionStatus.Success, "14123456", 3_000_000m, "{}",
            error: null, needsReview: false, Now, PaymentTransactionType.Return);

        Assert.Equal(PaymentTransactionType.Return, transaction.Type);
    }

    [Fact]
    public void Callback_AsACheckout_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentTransaction.Callback(
            null, PaymentGateway.VnPay, PaymentTransactionStatus.Failed, null, null, "{}", null, false, Now,
            PaymentTransactionType.Checkout));
}
