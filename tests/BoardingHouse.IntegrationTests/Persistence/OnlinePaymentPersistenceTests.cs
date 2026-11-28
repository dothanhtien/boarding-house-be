using Microsoft.EntityFrameworkCore;
using Npgsql;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Persistence;

public class OnlinePaymentPersistenceTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 3, 30, 0, TimeSpan.Zero);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid OrganizationId, Guid PaymentId, Guid StaffId)> SeedPaymentAsync()
    {
        await using var context = fixture.CreateContext();
        var (invoiceId, staffId, tenantId) = await TestInvoices.SeedAsync(context);
        var organizationId = await context.Invoices.Where(i => i.Id == invoiceId).Select(i => i.OrganizationId).SingleAsync();

        var payment = TestEntities.New<Payment>(new
        {
            InvoiceId = (Guid?)invoiceId,
            PayerUserId = (Guid?)tenantId,
            Amount = 2_000_000m,
            Method = PaymentMethod.VnPay,
            PaymentDate = Now,
            CreatedBy = tenantId
        });
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        return (organizationId, payment.Id, staffId);
    }

    private static OrganizationPaymentGateway Account(Guid organizationId, Guid createdBy, string merchantCode = "TESTCODE") =>
        TestEntities.New<OrganizationPaymentGateway>(new
        {
            OrganizationId = organizationId,
            Gateway = PaymentGateway.VnPay,
            MerchantCode = merchantCode,
            SecretEncrypted = "CfDJ8-ciphertext",
            IsActive = true,
            CreatedBy = createdBy
        });

    private static PaymentTransaction Callback(Guid? paymentId, PaymentTransactionStatus status, string? gatewayTransactionId) =>
        TestEntities.New<PaymentTransaction>(new
        {
            PaymentId = paymentId,
            Gateway = PaymentGateway.VnPay,
            Type = PaymentTransactionType.Callback,
            Status = status,
            GatewayTransactionId = gatewayTransactionId,
            WebhookPayload = """{"vnp_TxnRef":"x"}""",
            CreatedAt = Now
        });

    private static string? ConstraintOf(DbUpdateException ex) => (ex.InnerException as PostgresException)?.ConstraintName;

    [Fact]
    public async Task OrganizationPaymentGateway_RoundTrips()
    {
        var (organizationId, _, staffId) = await SeedPaymentAsync();
        var account = Account(organizationId, staffId);

        await using (var context = fixture.CreateContext())
        {
            context.OrganizationPaymentGateways.Add(account);
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var saved = await readContext.OrganizationPaymentGateways.SingleAsync(g => g.Id == account.Id);
        Assert.Equal(
            (organizationId, PaymentGateway.VnPay, "TESTCODE", "CfDJ8-ciphertext", true, staffId),
            (saved.OrganizationId, saved.Gateway, saved.MerchantCode, saved.SecretEncrypted, saved.IsActive, saved.CreatedBy));
        Assert.Equal("VNPAY", await readContext.Database
            .SqlQuery<string>($"SELECT gateway AS \"Value\" FROM organization_payment_gateways WHERE id = {account.Id}")
            .SingleAsync());
    }

    [Fact]
    public async Task OrganizationPaymentGateway_SecondAccountForTheSameGateway_IsRejected()
    {
        var (organizationId, _, staffId) = await SeedPaymentAsync();
        await using var context = fixture.CreateContext();
        context.OrganizationPaymentGateways.Add(Account(organizationId, staffId, "FIRST"));
        context.OrganizationPaymentGateways.Add(Account(organizationId, staffId, "SECOND"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(UniqueIndexNames.OrganizationPaymentGatewayOrganizationIdGateway, ConstraintOf(ex));
    }

    [Fact]
    public async Task OrganizationPaymentGateway_UnknownOrganization_IsRejected()
    {
        var (_, _, staffId) = await SeedPaymentAsync();
        await using var context = fixture.CreateContext();
        context.OrganizationPaymentGateways.Add(Account(Guid.NewGuid(), staffId));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal("fk_organization_payment_gateways_organizations_organization_id", ConstraintOf(ex));
    }

    [Fact]
    public async Task PaymentTransaction_RoundTripsWithJsonPayloads()
    {
        var (_, paymentId, _) = await SeedPaymentAsync();
        var checkout = TestEntities.New<PaymentTransaction>(new
        {
            PaymentId = (Guid?)paymentId,
            Gateway = PaymentGateway.VnPay,
            Type = PaymentTransactionType.Checkout,
            Status = PaymentTransactionStatus.Pending,
            Amount = (decimal?)2_000_000m,
            PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=x",
            ExpiresAt = (DateTimeOffset?)Now.AddMinutes(15),
            RequestPayload = """{"vnp_Amount":"200000000","vnp_TxnRef":"x"}""",
            CreatedAt = Now
        });

        await using (var context = fixture.CreateContext())
        {
            context.PaymentTransactions.Add(checkout);
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var saved = await readContext.PaymentTransactions.SingleAsync(t => t.Id == checkout.Id);
        Assert.Equal(paymentId, saved.PaymentId);
        Assert.Equal(PaymentGateway.VnPay, saved.Gateway);
        Assert.Equal(PaymentTransactionType.Checkout, saved.Type);
        Assert.Equal(PaymentTransactionStatus.Pending, saved.Status);
        Assert.Equal(2_000_000m, saved.Amount);
        Assert.Equal("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=x", saved.PaymentUrl);
        Assert.Equal(Now.AddMinutes(15), saved.ExpiresAt);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Null(saved.GatewayTransactionId);
        Assert.Null(saved.ResponsePayload);
        Assert.Null(saved.WebhookPayload);
        Assert.Null(saved.Error);
        Assert.False(saved.NeedsReview);
        Assert.Equal("200000000", await readContext.Database
            .SqlQuery<string>($"SELECT request_payload ->> 'vnp_Amount' AS \"Value\" FROM payment_transactions WHERE id = {checkout.Id}")
            .SingleAsync());
    }

    [Fact]
    public async Task PaymentTransaction_EnumsAreStoredAsTheirDbStrings()
    {
        var (_, paymentId, _) = await SeedPaymentAsync();
        var callback = Callback(paymentId, PaymentTransactionStatus.Success, "14123456");

        await using var context = fixture.CreateContext();
        context.PaymentTransactions.Add(callback);
        await context.SaveChangesAsync();

        Assert.Equal("VNPAY/CALLBACK/SUCCESS", await context.Database
            .SqlQuery<string>($"SELECT gateway || '/' || type || '/' || status AS \"Value\" FROM payment_transactions WHERE id = {callback.Id}")
            .SingleAsync());
    }

    [Fact]
    public async Task PaymentTransaction_FlaggedForReview_RoundTrips()
    {
        var (_, paymentId, _) = await SeedPaymentAsync();
        var callback = Callback(paymentId, PaymentTransactionStatus.Success, "14123456");
        TestEntities.Set(callback, new { NeedsReview = true, Amount = (decimal?)2_000_000m, Error = "Payment already voided" });

        await using (var context = fixture.CreateContext())
        {
            context.PaymentTransactions.Add(callback);
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var saved = await readContext.PaymentTransactions.SingleAsync(t => t.Id == callback.Id);
        Assert.True(saved.NeedsReview);
        Assert.Equal("Payment already voided", saved.Error);
        Assert.Equal("x", await readContext.Database
            .SqlQuery<string>($"SELECT webhook_payload ->> 'vnp_TxnRef' AS \"Value\" FROM payment_transactions WHERE id = {callback.Id}")
            .SingleAsync());
    }

    [Fact]
    public async Task PaymentTransaction_CallbackWithoutPayment_IsAllowed()
    {
        await using var context = fixture.CreateContext();
        context.PaymentTransactions.Add(Callback(paymentId: null, PaymentTransactionStatus.Failed, gatewayTransactionId: null));

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.PaymentTransactions.CountAsync());
    }

    [Fact]
    public async Task PaymentTransaction_CheckoutWithoutPayment_IsRejected()
    {
        await using var context = fixture.CreateContext();
        var checkout = Callback(paymentId: null, PaymentTransactionStatus.Pending, gatewayTransactionId: null);
        TestEntities.Set(checkout, new { Type = PaymentTransactionType.Checkout });
        context.PaymentTransactions.Add(checkout);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal("ck_payment_transactions_checkout_payment", ConstraintOf(ex));
    }

    [Fact]
    public async Task PaymentTransaction_UnknownPayment_IsRejected()
    {
        await using var context = fixture.CreateContext();
        context.PaymentTransactions.Add(Callback(Guid.NewGuid(), PaymentTransactionStatus.Failed, gatewayTransactionId: null));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal("fk_payment_transactions_payments_payment_id", ConstraintOf(ex));
    }

    [Fact]
    public async Task PaymentTransaction_TwoSuccessfulCallbacksOfOneGatewayTransaction_AreRejected()
    {
        var (_, paymentId, _) = await SeedPaymentAsync();
        await using var context = fixture.CreateContext();
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Success, "14123456"));
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Success, "14123456"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(UniqueIndexNames.PaymentTransactionGatewayGatewayTransactionId, ConstraintOf(ex));
    }

    [Fact]
    public async Task PaymentTransaction_FailedRowsMayRepeatAGatewayTransactionId()
    {
        var (_, paymentId, _) = await SeedPaymentAsync();
        await using var context = fixture.CreateContext();
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Failed, "14123456"));
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Failed, "14123456"));
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Success, "14123456"));

        await context.SaveChangesAsync();

        Assert.Equal(3, await context.PaymentTransactions.CountAsync());
    }

    [Fact]
    public async Task PaymentTransaction_SuccessfulRowsWithoutGatewayTransactionId_AreNotUnique()
    {
        var (_, paymentId, _) = await SeedPaymentAsync();
        await using var context = fixture.CreateContext();
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Success, gatewayTransactionId: null));
        context.PaymentTransactions.Add(Callback(paymentId, PaymentTransactionStatus.Success, gatewayTransactionId: null));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PaymentTransactions.CountAsync());
    }
}
