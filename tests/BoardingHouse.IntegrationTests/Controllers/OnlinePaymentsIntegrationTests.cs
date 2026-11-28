using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Application.Services.Security;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Services.PaymentGateways.VnPay;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class OnlinePaymentsIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string Password = "password123";
    private const string MerchantCode = "TESTCODE";
    private const string Secret = "TESTSECRET0123456789";

    private Guid _staffId;
    private Guid _organizationId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        var organization = TestEntities.New<Organization>(new { Name = "Org A", CreatedBy = staff.Id });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property A", CreatedBy = staff.Id });
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101", capacity: 4, createdBy: staff.Id);
        context.AddRange(staff, organization, property, room);
        await context.SaveChangesAsync();

        _staffId = staff.Id;
        _organizationId = organization.Id;
        _roomId = room.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal))
            .Split(';')[0]["accessToken=".Length..];

    private async Task<(HttpClient Client, Guid UserId)> RegisterAndLoginAsync(string fullName)
    {
        var email = $"user-{Guid.NewGuid():N}@test.com";
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = fullName
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return (client, user.Id);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    // An active lease of the room since 2026-01-01 with the tenant as representative, and its February invoice
    private async Task<Guid> SeedInvoiceAsync(Guid tenantId, InvoiceStatus status = InvoiceStatus.Pending, decimal paidAmount = 0)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.Users.SingleAsync(u => u.Id == tenantId);
        var room = await context.Rooms.SingleAsync(r => r.Id == _roomId);
        var start = new DateOnly(2026, 1, 1);

        var lease = Lease.CreateDraft(
            _organizationId, room, previousLeaseId: null, $"HD-{Guid.NewGuid():N}"[..10], start, start.AddYears(1),
            billingDay: null, monthlyRent: 3_000_000, depositAmount: 3_000_000,
            [new TenantChange { UserId = tenantId, IsRepresentative = true }],
            new Dictionary<Guid, User> { [tenantId] = tenant }, [], DateTimeOffset.UtcNow, _staffId);
        TestEntities.Set(lease, new { Status = LeaseStatus.Active });

        var invoice = TestEntities.New<Invoice>(new
        {
            OrganizationId = _organizationId,
            LeaseId = lease.Id,
            InvoiceNumber = $"INV-2026-02-{Guid.NewGuid():N}"[..24],
            BillingMonth = BillingMonth.FromFirstDay(new DateOnly(2026, 2, 1)),
            // far ahead: the checkout's RefreshStatus must not add a late fee to these tests
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
            Subtotal = 3_000_000m,
            TotalAmount = 3_000_000m,
            PaidAmount = paidAmount,
            Status = status,
            CreatedBy = _staffId
        });
        context.AddRange(lease, invoice);
        await context.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task ConfigureVnPayAsync(bool isActive = true)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        context.OrganizationPaymentGateways.Add(OrganizationPaymentGateway.Create(
            _organizationId, PaymentGateway.VnPay, MerchantCode, protector.Protect(Secret), isActive, _staffId));
        await context.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> CheckoutAsync(HttpClient client, Guid invoiceId) =>
        client.PostAsJsonAsync($"/api/me/invoices/{invoiceId}/online-payments", new { gateway = "vnPay" });

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task ExecuteAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // A tenant with an unpaid 3,000,000 invoice, VNPay configured, and a checkout started
    private async Task<(HttpClient Client, Guid InvoiceId, Guid PaymentId)> StartCheckoutAsync()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync();
        var checkout = await ReadDataAsync<OnlineCheckoutResponse>(await CheckoutAsync(client, invoiceId));
        return (client, invoiceId, checkout.PaymentId);
    }

    private static Dictionary<string, string> IpnFields(
        Guid paymentId, decimal amount = 3_000_000m, string responseCode = "00", string transactionNo = "14123456") => new()
    {
        ["vnp_Amount"] = VnPayGateway.ToVnPayAmount(amount),
        ["vnp_BankCode"] = "NCB",
        ["vnp_OrderInfo"] = "Invoice INV-2026-02",
        ["vnp_PayDate"] = VnPayGateway.FormatDate(DateTimeOffset.UtcNow),
        ["vnp_ResponseCode"] = responseCode,
        ["vnp_TmnCode"] = MerchantCode,
        ["vnp_TransactionNo"] = transactionNo,
        ["vnp_TransactionStatus"] = responseCode == "00" ? "00" : "02",
        ["vnp_TxnRef"] = VnPayGateway.TxnRefOf(paymentId)
    };

    // Signs like VNPay does and returns RspCode
    private async Task<string> SendIpnAsync(Dictionary<string, string> fields, string secret = Secret, bool post = false)
    {
        var data = VnPaySignature.BuildData(fields);
        var url = $"/api/payment-gateways/vnpay/ipn?{data}&{VnPaySignature.SecureHashField}={VnPaySignature.Sign(data, secret)}";
        var client = factory.CreateClient();

        var response = post ? await client.PostAsync(url, content: null) : await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("RspCode").GetString()!;
    }

    private Task<List<PaymentTransaction>> CallbacksAsync() =>
        QueryAsync(c => c.PaymentTransactions
            .Where(t => t.Type == PaymentTransactionType.Callback)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync());

    // Integration tests call the job directly: the Hangfire server is off (BackgroundJobs:Enabled = false)
    private async Task RunExpiryJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VoidExpiredOnlinePaymentsJob>().RunAsync();
    }

    private Task AgePaymentAsync(Guid paymentId, TimeSpan age) =>
        ExecuteAsync(c => c.Database.ExecuteSqlAsync(
            $"UPDATE payments SET created_at = {DateTimeOffset.UtcNow - age} WHERE id = {paymentId}"));

    private Task<Payment> PaymentAsync(Guid paymentId) => QueryAsync(c => c.Payments.SingleAsync(p => p.Id == paymentId));

    [Fact]
    public async Task GetGateways_ActiveVnPay_ListsIt()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync();

        var response = await client.GetAsync($"/api/me/invoices/{invoiceId}/online-payment-gateways");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([PaymentGateway.VnPay], await ReadDataAsync<List<PaymentGateway>>(response));
    }

    [Fact]
    public async Task GetGateways_InactiveVnPay_ReturnsNone()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync(isActive: false);

        var gateways = await ReadDataAsync<List<PaymentGateway>>(
            await client.GetAsync($"/api/me/invoices/{invoiceId}/online-payment-gateways"));

        Assert.Empty(gateways);
    }

    [Fact]
    public async Task GetGateways_OtherTenantsInvoice_Returns404()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var invoiceId = await SeedInvoiceAsync(tenantB);
        await ConfigureVnPayAsync();

        var response = await client.GetAsync($"/api/me/invoices/{invoiceId}/online-payment-gateways");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_CreatesAPendingPaymentAndASignedUrl()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync();

        var response = await CheckoutAsync(client, invoiceId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checkout = await ReadDataAsync<OnlineCheckoutResponse>(response);
        Assert.Equal((PaymentGateway.VnPay, 3_000_000m), (checkout.Gateway, checkout.Amount));
        Assert.InRange(checkout.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", checkout.PaymentUrl);

        var query = QueryHelpers.ParseQuery(new Uri(checkout.PaymentUrl).Query)
            .ToDictionary(q => q.Key, q => q.Value.ToString());
        Assert.True(VnPaySignature.Verify(query, Secret));
        Assert.Equal((MerchantCode, "300000000", checkout.PaymentId.ToString("N")),
            (query["vnp_TmnCode"], query["vnp_Amount"], query["vnp_TxnRef"]));
        Assert.Equal("https://app.test/payments/vnpay-return", query["vnp_ReturnUrl"]);

        var payment = await QueryAsync(c => c.Payments.SingleAsync(p => p.Id == checkout.PaymentId));
        Assert.Equal((invoiceId, PaymentStatus.Pending, PaymentMethod.VnPay, 3_000_000m),
            (payment.InvoiceId!.Value, payment.Status, payment.Method, payment.Amount));
        Assert.Equal((tenantId, tenantId), (payment.PayerUserId!.Value, payment.CreatedBy));

        var transaction = await QueryAsync(c => c.PaymentTransactions.SingleAsync());
        Assert.Equal((PaymentTransactionType.Checkout, PaymentTransactionStatus.Pending, checkout.PaymentUrl),
            (transaction.Type, transaction.Status, transaction.PaymentUrl));
        Assert.DoesNotContain(Secret, transaction.RequestPayload!);
        Assert.DoesNotContain(query[VnPaySignature.SecureHashField], transaction.RequestPayload!);
    }

    [Fact]
    public async Task Checkout_Twice_ReturnsTheSamePayment()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync();

        var first = await ReadDataAsync<OnlineCheckoutResponse>(await CheckoutAsync(client, invoiceId));
        var second = await ReadDataAsync<OnlineCheckoutResponse>(await CheckoutAsync(client, invoiceId));

        Assert.Equal((first.PaymentId, first.PaymentUrl), (second.PaymentId, second.PaymentUrl));
        Assert.Equal(1, await QueryAsync(c => c.Payments.CountAsync()));
    }

    [Fact]
    public async Task Checkout_TwoClicksAtOnce_CreateOnePayment()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync();

        var responses = await Task.WhenAll(CheckoutAsync(client, invoiceId), CheckoutAsync(client, invoiceId));

        // The second one waits on the invoice lock; it either reuses the link or, if the link is not saved yet, gets 409
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(1, await QueryAsync(c => c.Payments.CountAsync()));
        Assert.Equal(1, await QueryAsync(c => c.PaymentTransactions.CountAsync()));
    }

    [Fact]
    public async Task Checkout_CancelledInvoice_Returns409()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId, InvoiceStatus.Cancelled);
        await ConfigureVnPayAsync();

        var response = await CheckoutAsync(client, invoiceId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await QueryAsync(c => c.Payments.CountAsync()));
    }

    [Fact]
    public async Task Checkout_PartiallyPaidInvoice_RequestsTheRemainingAmount()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId, InvoiceStatus.Partial, paidAmount: 1_200_000m);
        await ConfigureVnPayAsync();

        var checkout = await ReadDataAsync<OnlineCheckoutResponse>(await CheckoutAsync(client, invoiceId));

        Assert.Equal(1_800_000m, checkout.Amount);
    }

    [Fact]
    public async Task Checkout_GatewayNotConfigured_Returns409()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);

        var response = await CheckoutAsync(client, invoiceId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await QueryAsync(c => c.Payments.CountAsync()));
    }

    [Fact]
    public async Task Checkout_GatewayInactive_Returns409()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);
        await ConfigureVnPayAsync(isActive: false);

        var response = await CheckoutAsync(client, invoiceId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_PaidInvoice_Returns409()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId, InvoiceStatus.Paid, paidAmount: 3_000_000m);
        await ConfigureVnPayAsync();

        var response = await CheckoutAsync(client, invoiceId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_OtherTenantsInvoice_Returns404()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var invoiceId = await SeedInvoiceAsync(tenantB);
        await ConfigureVnPayAsync();

        var response = await CheckoutAsync(client, invoiceId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithoutGateway_Returns400()
    {
        var (client, tenantId) = await RegisterAndLoginAsync("Tenant A");
        var invoiceId = await SeedInvoiceAsync(tenantId);

        var response = await client.PostAsJsonAsync($"/api/me/invoices/{invoiceId}/online-payments", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ipn_Success_ConfirmsThePaymentAndPaysTheInvoice()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId)));

        var payment = await QueryAsync(c => c.Payments.SingleAsync(p => p.Id == paymentId));
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Null(payment.ConfirmedBy);
        Assert.NotNull(payment.ConfirmedAt);
        Assert.Equal("14123456", payment.TransactionReference);
        Assert.Equal(SentinelActors.System, payment.UpdatedBy);

        var invoice = await QueryAsync(c => c.Invoices.SingleAsync(i => i.Id == invoiceId));
        Assert.Equal((InvoiceStatus.Paid, 3_000_000m), (invoice.Status, invoice.PaidAmount));

        var callback = Assert.Single(await CallbacksAsync());
        Assert.Equal((paymentId, PaymentTransactionStatus.Success, "14123456", false),
            (callback.PaymentId!.Value, callback.Status, callback.GatewayTransactionId, callback.NeedsReview));
        // jsonb: Postgres reformats the text, so compare the parsed value
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(callback.WebhookPayload!)!;
        Assert.Equal("***", payload[VnPaySignature.SecureHashField]);
        Assert.Equal("14123456", payload["vnp_TransactionNo"]);
    }

    [Fact]
    public async Task Ipn_Post_IsAlsoAccepted()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId), post: true));

        Assert.Equal(PaymentStatus.Confirmed, await QueryAsync(c => c.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync()));
    }

    [Fact]
    public async Task Ipn_SameTransactionTwice_Returns02AndCountsOnce()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();
        await SendIpnAsync(IpnFields(paymentId));

        Assert.Equal("02", await SendIpnAsync(IpnFields(paymentId)));

        Assert.Equal(3_000_000m, await QueryAsync(c => c.Invoices.Where(i => i.Id == invoiceId).Select(i => i.PaidAmount).SingleAsync()));
        Assert.Single(await CallbacksAsync());
    }

    [Fact]
    public async Task Ipn_WrongSignature_Returns97AndChangesNothing()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();

        Assert.Equal("97", await SendIpnAsync(IpnFields(paymentId), secret: "OTHERSECRET"));

        Assert.Equal(PaymentStatus.Pending, await QueryAsync(c => c.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync()));
        var callback = Assert.Single(await CallbacksAsync());
        Assert.Equal((PaymentTransactionStatus.Failed, "Invalid signature"), (callback.Status, callback.Error));
    }

    [Fact]
    public async Task Ipn_FailedPaymentWithWrongSignature_DoesNotVoidThePayment()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();

        Assert.Equal("97", await SendIpnAsync(IpnFields(paymentId, responseCode: "24"), secret: "OTHERSECRET"));

        Assert.Equal(PaymentStatus.Pending, await QueryAsync(c => c.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync()));
    }

    [Fact]
    public async Task Ipn_WrongAmount_Returns04AndFlagsAPaidMismatch()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();

        Assert.Equal("04", await SendIpnAsync(IpnFields(paymentId, amount: 1_000_000m)));

        Assert.Equal(PaymentStatus.Pending, await QueryAsync(c => c.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync()));
        var callback = Assert.Single(await CallbacksAsync());
        Assert.Equal((PaymentTransactionStatus.Failed, 1_000_000m, true), (callback.Status, callback.Amount!.Value, callback.NeedsReview));
    }

    [Fact]
    public async Task Ipn_UnknownPayment_Returns01()
    {
        await StartCheckoutAsync();

        Assert.Equal("01", await SendIpnAsync(IpnFields(Guid.NewGuid())));

        var callback = Assert.Single(await CallbacksAsync());
        Assert.Null(callback.PaymentId);
        Assert.Equal("Unknown payment reference", callback.Error);
    }

    [Fact]
    public async Task Ipn_FailedPayment_VoidsItAndAllowsANewCheckout()
    {
        var (client, invoiceId, paymentId) = await StartCheckoutAsync();

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId, responseCode: "24")));

        var payment = await QueryAsync(c => c.Payments.SingleAsync(p => p.Id == paymentId));
        Assert.Equal((PaymentStatus.Voided, Payment.GatewayFailedReason), (payment.Status, payment.VoidReason));
        Assert.Null(payment.VoidedBy);

        var next = await ReadDataAsync<OnlineCheckoutResponse>(await CheckoutAsync(client, invoiceId));
        Assert.NotEqual(paymentId, next.PaymentId);
    }

    [Fact]
    public async Task Ipn_SuccessForAnInvoiceAlreadyPaid_VoidsThePaymentAndFlagsIt()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();
        // Staff took the rent in cash meanwhile
        await ExecuteAsync(async c =>
        {
            var invoice = await c.Invoices.SingleAsync(i => i.Id == invoiceId);
            TestEntities.Set(invoice, new { PaidAmount = 3_000_000m, Status = InvoiceStatus.Paid });
            await c.SaveChangesAsync();
        });

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId)));

        var payment = await QueryAsync(c => c.Payments.SingleAsync(p => p.Id == paymentId));
        Assert.Equal((PaymentStatus.Voided, Payment.GatewayNotAppliedReason), (payment.Status, payment.VoidReason));
        Assert.Equal(3_000_000m, await QueryAsync(c => c.Invoices.Where(i => i.Id == invoiceId).Select(i => i.PaidAmount).SingleAsync()));
        var callback = Assert.Single(await CallbacksAsync());
        Assert.Equal((PaymentTransactionStatus.Success, true), (callback.Status, callback.NeedsReview));
    }

    [Fact]
    public async Task Ipn_SuccessAfterThePaymentWasVoided_FlagsIt()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();
        await SendIpnAsync(IpnFields(paymentId, responseCode: "24"));

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId, transactionNo: "14123999")));

        Assert.Equal(PaymentStatus.Voided, await QueryAsync(c => c.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync()));
        Assert.Equal(0m, await QueryAsync(c => c.Invoices.Where(i => i.Id == invoiceId).Select(i => i.PaidAmount).SingleAsync()));
        var flagged = Assert.Single(await CallbacksAsync(), t => t.NeedsReview);
        Assert.Equal(("14123999", "Paid after the payment was voided"), (flagged.GatewayTransactionId, flagged.Error));
    }

    [Fact]
    public async Task Ipn_GatewayTurnedOffAfterCheckout_StillConfirms()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();
        await ExecuteAsync(async c =>
        {
            var account = await c.OrganizationPaymentGateways.SingleAsync();
            TestEntities.Set(account, new { IsActive = false });
            await c.SaveChangesAsync();
        });

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId)));

        Assert.Equal(InvoiceStatus.Paid, await QueryAsync(c => c.Invoices.Where(i => i.Id == invoiceId).Select(i => i.Status).SingleAsync()));
    }

    [Fact]
    public async Task Ipn_SuccessForACancelledInvoice_VoidsThePaymentAndFlagsIt()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();
        await ExecuteAsync(async c =>
        {
            var invoice = await c.Invoices.SingleAsync(i => i.Id == invoiceId);
            TestEntities.Set(invoice, new { Status = InvoiceStatus.Cancelled });
            await c.SaveChangesAsync();
        });

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId)));

        var payment = await QueryAsync(c => c.Payments.SingleAsync(p => p.Id == paymentId));
        Assert.Equal((PaymentStatus.Voided, Payment.GatewayNotAppliedReason), (payment.Status, payment.VoidReason));
        var callback = Assert.Single(await CallbacksAsync());
        Assert.Equal((PaymentTransactionStatus.Success, true), (callback.Status, callback.NeedsReview));
        Assert.NotNull(callback.Error);
    }

    [Fact]
    public async Task Ipn_UnreadableTxnRef_Returns01()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();
        var fields = IpnFields(paymentId);
        fields["vnp_TxnRef"] = "not-a-payment";

        Assert.Equal("01", await SendIpnAsync(fields));

        Assert.Equal(PaymentStatus.Pending, await QueryAsync(c => c.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync()));
    }

    [Fact]
    public async Task Ipn_SameTransactionTwiceAtOnce_ConfirmsOnce()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();

        var codes = await Task.WhenAll(SendIpnAsync(IpnFields(paymentId)), SendIpnAsync(IpnFields(paymentId)));

        // The second delivery waits on the invoice lock and then sees the first one's SUCCESS row
        Assert.Equal(["00", "02"], codes.Order());
        Assert.Equal(3_000_000m, await QueryAsync(c => c.Invoices.Where(i => i.Id == invoiceId).Select(i => i.PaidAmount).SingleAsync()));
        Assert.Single(await CallbacksAsync());
    }

    [Fact]
    public async Task ExpiryJob_VoidsAGatewayPaymentPendingForMoreThan30Minutes()
    {
        var (client, invoiceId, paymentId) = await StartCheckoutAsync();
        await AgePaymentAsync(paymentId, TimeSpan.FromMinutes(31));

        await RunExpiryJobAsync();

        var payment = await PaymentAsync(paymentId);
        Assert.Equal((PaymentStatus.Voided, Payment.GatewayExpiredReason), (payment.Status, payment.VoidReason));
        Assert.Null(payment.VoidedBy);
        Assert.Equal(SentinelActors.System, payment.UpdatedBy);

        var next = await ReadDataAsync<OnlineCheckoutResponse>(await CheckoutAsync(client, invoiceId));
        Assert.NotEqual(paymentId, next.PaymentId);
    }

    [Fact]
    public async Task ExpiryJob_KeepsAPaymentWithinTheGracePeriod()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();
        await AgePaymentAsync(paymentId, TimeSpan.FromMinutes(20));

        await RunExpiryJobAsync();

        Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(paymentId)).Status);
    }

    [Fact]
    public async Task ExpiryJob_LeavesConfirmedAndCashPaymentsAlone()
    {
        var (_, invoiceId, paymentId) = await StartCheckoutAsync();
        await SendIpnAsync(IpnFields(paymentId, amount: 3_000_000m));
        await AgePaymentAsync(paymentId, TimeSpan.FromHours(2));

        var cash = TestEntities.New<Payment>(new
        {
            InvoiceId = (Guid?)invoiceId,
            Amount = 1_000m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTimeOffset.UtcNow,
            CreatedBy = _staffId
        });
        await ExecuteAsync(async c =>
        {
            c.Payments.Add(cash);
            await c.SaveChangesAsync();
        });
        await AgePaymentAsync(cash.Id, TimeSpan.FromHours(2));

        await RunExpiryJobAsync();

        Assert.Equal(PaymentStatus.Confirmed, (await PaymentAsync(paymentId)).Status);
        Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(cash.Id)).Status);
    }

    [Fact]
    public async Task Ipn_SuccessAfterTheExpiryJob_IsFlaggedForReview()
    {
        var (_, _, paymentId) = await StartCheckoutAsync();
        await AgePaymentAsync(paymentId, TimeSpan.FromMinutes(31));
        await RunExpiryJobAsync();

        Assert.Equal("00", await SendIpnAsync(IpnFields(paymentId)));

        Assert.Equal(PaymentStatus.Voided, (await PaymentAsync(paymentId)).Status);
        Assert.True(Assert.Single(await CallbacksAsync()).NeedsReview);
    }
}
