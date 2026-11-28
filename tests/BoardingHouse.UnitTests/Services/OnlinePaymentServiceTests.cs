using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Application.Services.Security;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class OnlinePaymentServiceTests
{
    private readonly Mock<IPaymentGateway> _gateway = new();
    private readonly Mock<ITenantPortalQueries> _tenantPortalQueries = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<IPaymentTransactionRepository> _paymentTransactionRepository = new();
    private readonly Mock<IOrganizationPaymentGatewayRepository> _organizationPaymentGatewayRepository = new();
    private readonly Mock<ISecretProtector> _secretProtector = new();
    private readonly Mock<IPropertyRepository> _propertyRepository = new();
    private readonly Mock<IOrganizationSettingsRepository> _organizationSettingsRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CurrentUserAccessor _currentUserAccessor = new();
    private readonly List<PaymentTransaction> _transactions = [];
    private readonly Invoice _invoice;
    private readonly OnlinePaymentService _service;
    private Payment? _payment;

    public OnlinePaymentServiceTests()
    {
        _currentUserAccessor.User = TestEntities.New<User>(new
        {
            FullName = "Tenant", PasswordHash = "hash", CreatedBy = SentinelActors.System
        });

        _invoice = NewInvoice();
        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(_invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_invoice);

        _tenantPortalQueries
            .Setup(q => q.IsInvoiceVisibleAsync(_currentUserAccessor.User.Id, _invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _organizationPaymentGatewayRepository
            .Setup(r => r.GetAsync(_invoice.OrganizationId, PaymentGateway.VnPay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationPaymentGateway.Create(
                _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: true, Guid.NewGuid()));
        _secretProtector.Setup(p => p.Unprotect("cipher")).Returns("TESTSECRET");

        _paymentRepository
            .Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback((Payment payment, CancellationToken _) => _payment = payment);
        _paymentRepository
            .Setup(r => r.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _payment);
        _paymentTransactionRepository
            .Setup(r => r.AddAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentTransaction transaction, CancellationToken _) => _transactions.Add(transaction));

        // Run the delegates inline, as the real unit of work does inside its transaction
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<It.IsAnyType>>>(), It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation =>
                ((Delegate)invocation.Arguments[0]).DynamicInvoke(invocation.Arguments[1])!));
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct));

        _gateway.SetupGet(g => g.Gateway).Returns(PaymentGateway.VnPay);

        _service = new OnlinePaymentService(
            [_gateway.Object],
            _tenantPortalQueries.Object,
            _invoiceRepository.Object,
            _paymentRepository.Object,
            _paymentTransactionRepository.Object,
            _organizationPaymentGatewayRepository.Object,
            _secretProtector.Object,
            new InvoiceLateFeeResolver(_propertyRepository.Object, _organizationSettingsRepository.Object),
            _unitOfWork.Object,
            _currentUserAccessor,
            NullLogger<OnlinePaymentService>.Instance);
    }

    // Rent 3,000,000, no VAT, due in 10 days
    private static Invoice NewInvoice()
    {
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        IReadOnlyList<InvoiceLine> lines = [new("Rent", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, Guid.NewGuid())];
        var calculation = new InvoiceCalculation
        {
            BillingMonth = BillingMonth.Of(dueDate),
            DueDate = dueDate,
            VatRate = 0,
            DiscountAmount = 0,
            Lines = lines,
            Totals = InvoiceTotals.Compute(lines.Select(l => (l.SourceType, l.Amount)), 0, 0, 0),
            Errors = [],
            Warnings = []
        };
        return Invoice.Create(
            calculation, Guid.NewGuid(), Guid.NewGuid(), "INV-202611-0001",
            discountReason: null, notes: null, DateTimeOffset.UtcNow, SentinelActors.System);
    }

    private Task<OnlineCheckoutResponse> CheckoutAsync() =>
        _service.CreateMyCheckoutAsync(
            _invoice.Id, new CreateOnlineCheckoutRequest { Gateway = PaymentGateway.VnPay }, "127.0.0.1");

    private void GatewayThrows(bool isTimeout) =>
        _gateway
            .Setup(g => g.CreateCheckoutAsync(It.IsAny<GatewayCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentGatewayException("resultCode 1001", isTimeout, requestPayload: """{"orderId":"x"}"""));

    [Fact]
    public async Task CreateMyCheckout_GatewayAccepts_RecordsAPendingCheckout()
    {
        GatewayCheckoutRequest? sent = null;
        _gateway
            .Setup(g => g.CreateCheckoutAsync(It.IsAny<GatewayCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .Callback((GatewayCheckoutRequest request, CancellationToken _) => sent = request)
            .ReturnsAsync(new GatewayCheckout("https://pay.test/x", "{}", null));

        var response = await CheckoutAsync();

        Assert.Equal(("https://pay.test/x", 3_000_000m), (response.PaymentUrl, response.Amount));
        Assert.Equal(_payment!.Id, response.PaymentId);
        Assert.Equal((PaymentStatus.Pending, PaymentMethod.VnPay), (_payment.Status, _payment.Method));
        Assert.Equal(_currentUserAccessor.User!.Id, _payment.PayerUserId);
        Assert.Equal(new GatewayCredentials("TESTCODE", "TESTSECRET"), sent!.Credentials);
        Assert.Equal("Invoice INV-202611-0001", sent.OrderInfo);
        Assert.Equal(sent.CreatedAt + Payment.GatewayCheckoutLifetime, sent.ExpiresAt);
        var transaction = Assert.Single(_transactions);
        Assert.Equal((PaymentTransactionType.Checkout, PaymentTransactionStatus.Pending), (transaction.Type, transaction.Status));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateMyCheckout_GatewayRefuses_VoidsThePaymentAndReturns502()
    {
        GatewayThrows(isTimeout: false);

        var ex = await Assert.ThrowsAsync<AppBadGatewayException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.GatewayUnavailableMessage, ex.Message);
        Assert.Equal(PaymentStatus.Voided, _payment!.Status);
        Assert.Equal(Payment.GatewayRequestFailedReason, _payment.VoidReason);
        Assert.Null(_payment.VoidedBy);
        var transaction = Assert.Single(_transactions);
        Assert.Equal((PaymentTransactionType.Checkout, PaymentTransactionStatus.Failed), (transaction.Type, transaction.Status));
        Assert.Equal("resultCode 1001", transaction.Error);
        Assert.Equal("""{"orderId":"x"}""", transaction.RequestPayload);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task CreateMyCheckout_GatewayTimesOut_KeepsThePaymentPending()
    {
        GatewayThrows(isTimeout: true);

        await Assert.ThrowsAsync<AppBadGatewayException>(CheckoutAsync);

        Assert.Equal(PaymentStatus.Pending, _payment!.Status);
        Assert.Equal(PaymentTransactionStatus.Failed, Assert.Single(_transactions).Status);
    }

    [Fact]
    public async Task CreateMyCheckout_PendingPaymentWithoutLink_Returns409()
    {
        var pending = TestEntities.New<Payment>(new
        {
            InvoiceId = (Guid?)_invoice.Id, Amount = 3_000_000m, Method = PaymentMethod.VnPay, CreatedBy = Guid.NewGuid()
        });
        _paymentRepository
            .Setup(r => r.FindPendingGatewayPaymentAsync(_invoice.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var ex = await Assert.ThrowsAsync<AppConflictException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.PaymentInProgressMessage, ex.Message);
        Assert.Null(_payment);
        _gateway.Verify(
            g => g.CreateCheckoutAsync(It.IsAny<GatewayCheckoutRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateMyCheckout_InvoiceNotVisible_Returns404()
    {
        _tenantPortalQueries
            .Setup(q => q.IsInvoiceVisibleAsync(It.IsAny<Guid>(), _invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<AppNotFoundException>(CheckoutAsync);

        Assert.Null(_payment);
    }

    private Payment SetupPendingPayment(PaymentMethod method, DateTimeOffset? linkExpiresAt)
    {
        var pending = TestEntities.New<Payment>(new
        {
            InvoiceId = (Guid?)_invoice.Id, Amount = 3_000_000m, Method = method, CreatedBy = Guid.NewGuid()
        });
        _paymentRepository
            .Setup(r => r.FindPendingGatewayPaymentAsync(_invoice.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        if (linkExpiresAt is { } expiresAt)
        {
            var now = DateTimeOffset.UtcNow;
            _paymentTransactionRepository
                .Setup(r => r.GetLatestCheckoutAsync(pending.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PaymentTransaction.Checkout(
                    pending.Id, PaymentGateway.VnPay, 3_000_000m, "https://pay.test/old", expiresAt, "{}", null, now));
        }

        return pending;
    }

    [Fact]
    public async Task CreateMyCheckout_PendingPaymentWithLiveLink_ReturnsItWithoutCallingTheGateway()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var pending = SetupPendingPayment(PaymentMethod.VnPay, expiresAt);

        var response = await CheckoutAsync();

        Assert.Equal(pending.Id, response.PaymentId);
        Assert.Equal(("https://pay.test/old", 3_000_000m, expiresAt), (response.PaymentUrl, response.Amount, response.ExpiresAt));
        Assert.Null(_payment);
        Assert.Empty(_transactions);
        _gateway.Verify(
            g => g.CreateCheckoutAsync(It.IsAny<GatewayCheckoutRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateMyCheckout_PendingPaymentOfAnotherGateway_Returns409()
    {
        SetupPendingPayment(PaymentMethod.Momo, DateTimeOffset.UtcNow.AddMinutes(10));

        var ex = await Assert.ThrowsAsync<AppConflictException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.PaymentInProgressMessage, ex.Message);
        Assert.Null(_payment);
    }

    [Fact]
    public async Task CreateMyCheckout_PendingPaymentWithExpiredLink_Returns409()
    {
        SetupPendingPayment(PaymentMethod.VnPay, DateTimeOffset.UtcNow.AddSeconds(-1));

        var ex = await Assert.ThrowsAsync<AppConflictException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.PaymentInProgressMessage, ex.Message);
        Assert.Null(_payment);
    }

    [Fact]
    public async Task CreateMyCheckout_GatewayWithoutAdapter_Returns409BeforeTouchingTheInvoice()
    {
        var ex = await Assert.ThrowsAsync<AppConflictException>(() => _service.CreateMyCheckoutAsync(
            _invoice.Id, new CreateOnlineCheckoutRequest { Gateway = (PaymentGateway)99 }, "127.0.0.1"));

        Assert.Equal(OrganizationPaymentGateway.NotAvailableMessage, ex.Message);
        _invoiceRepository.Verify(
            r => r.GetAggregateForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupAccounts(params OrganizationPaymentGateway[] accounts)
    {
        _invoiceRepository
            .Setup(r => r.GetOrganizationIdAsync(_invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_invoice.OrganizationId);
        _organizationPaymentGatewayRepository
            .Setup(r => r.ListByOrganizationIdAsync(_invoice.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. accounts]);
    }

    [Fact]
    public async Task GetMyGateways_ActiveAccountWithAnAdapter_IsListed()
    {
        SetupAccounts(OrganizationPaymentGateway.Create(
            _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: true, Guid.NewGuid()));

        Assert.Equal([PaymentGateway.VnPay], await _service.GetMyGatewaysAsync(_invoice.Id));
    }

    [Fact]
    public async Task GetMyGateways_SkipsInactiveAccountsAndGatewaysWithoutAnAdapter()
    {
        SetupAccounts(
            OrganizationPaymentGateway.Create(
                _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: false, Guid.NewGuid()),
            TestEntities.New<OrganizationPaymentGateway>(new
            {
                OrganizationId = _invoice.OrganizationId, Gateway = (PaymentGateway)99, IsActive = true,
                MerchantCode = "X", SecretEncrypted = "cipher", CreatedBy = Guid.NewGuid()
            }));

        Assert.Empty(await _service.GetMyGatewaysAsync(_invoice.Id));
    }

    [Fact]
    public async Task GetMyGateways_InvoiceNotVisible_Returns404()
    {
        _tenantPortalQueries
            .Setup(q => q.IsInvoiceVisibleAsync(It.IsAny<Guid>(), _invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<AppNotFoundException>(() => _service.GetMyGatewaysAsync(_invoice.Id));

        _organizationPaymentGatewayRepository.Verify(
            r => r.ListByOrganizationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
