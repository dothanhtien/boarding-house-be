using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.DTOs.TenantPortal;
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
                _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: true, Guid.NewGuid(), DateTimeOffset.UtcNow));
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

    private Task<MyOnlineCheckoutResponse> CheckoutAsync() =>
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
        Assert.Equal(_invoice.Id, sent.InvoiceId);
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
                    pending.Id, PaymentGateway.VnPay, 3_000_000m, "https://pay.test/old", expiresAt, "{}", null,
                    OrganizationPaymentGateway.FirstCredentialsVersion, now));
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
    public async Task CreateMyCheckout_PendingPaymentWithLinkAboutToExpire_Returns409()
    {
        SetupPendingPayment(
            PaymentMethod.VnPay,
            DateTimeOffset.UtcNow + OnlinePaymentService.MinReusedCheckoutLifetime - TimeSpan.FromSeconds(10));

        var ex = await Assert.ThrowsAsync<AppConflictException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.PaymentInProgressMessage, ex.Message);
        Assert.Null(_payment);
    }

    [Fact]
    public async Task CreateMyCheckout_LiveLinkButAmountDueChanged_Returns409()
    {
        SetupPendingPayment(PaymentMethod.VnPay, DateTimeOffset.UtcNow.AddMinutes(10));
        TestEntities.Set(_invoice, new { TotalAmount = 2_500_000m });

        var ex = await Assert.ThrowsAsync<AppConflictException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.PaymentInProgressMessage, ex.Message);
        Assert.Null(_payment);
    }

    [Fact]
    public async Task CreateMyCheckout_LiveLinkButInvoiceCancelled_Returns409()
    {
        SetupPendingPayment(PaymentMethod.VnPay, DateTimeOffset.UtcNow.AddMinutes(10));
        _invoice.Cancel("Issued by mistake", Guid.NewGuid(), DateTimeOffset.UtcNow);

        var ex = await Assert.ThrowsAsync<DomainException>(CheckoutAsync);

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Null(_payment);
    }

    [Fact]
    public async Task CreateMyCheckout_LooksForPendingPaymentsTheExpiryJobHasNotVoidedYet()
    {
        var before = DateTimeOffset.UtcNow;
        _gateway
            .Setup(g => g.CreateCheckoutAsync(It.IsAny<GatewayCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GatewayCheckout("https://pay.test/x", "{}", null));

        await CheckoutAsync();

        _paymentRepository.Verify(r => r.FindPendingGatewayPaymentAsync(
            _invoice.Id,
            It.Is<DateTimeOffset>(cutoff => cutoff <= DateTimeOffset.UtcNow - Payment.GatewayPendingWindow
                && cutoff >= before - Payment.GatewayPendingWindow),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CreateMyCheckout_ManualPaymentWaitingForStaff_Returns409()
    {
        _paymentRepository
            .Setup(r => r.ExistsPendingManualPaymentAsync(_invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<AppConflictException>(CheckoutAsync);

        Assert.Equal(OnlinePaymentService.PaymentAwaitingConfirmationMessage, ex.Message);
        Assert.Null(_payment);
        _gateway.Verify(
            g => g.CreateCheckoutAsync(It.IsAny<GatewayCheckoutRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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
            _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: true, Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal(
            [new MyOnlinePaymentGatewayResponse { Gateway = PaymentGateway.VnPay }], await _service.GetMyGatewaysAsync(_invoice.Id));
    }

    [Fact]
    public async Task GetMyGateways_SkipsInactiveAccountsAndGatewaysWithoutAnAdapter()
    {
        SetupAccounts(
            OrganizationPaymentGateway.Create(
                _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: false, Guid.NewGuid(), DateTimeOffset.UtcNow),
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

    private static readonly GatewayCallback IpnCallback = new(new Dictionary<string, string>(), Body: null);

    // The response body names the outcome, so a test can tell which one the service picked
    private void SetupCallback(Guid? paymentId)
    {
        _gateway
            .Setup(g => g.ParseCallback(IpnCallback))
            .Returns(new GatewayCallbackData(paymentId, "14123456", 3_000_000m, IsSuccess: true, "{}"));
        _gateway
            .Setup(g => g.BuildCallbackResponse(It.IsAny<GatewayCallbackOutcome>()))
            .Returns((GatewayCallbackOutcome outcome) => new GatewayHttpResponse(200, "application/json", outcome.ToString()));
    }

    [Fact]
    public async Task HandleCallback_UnsupportedGateway_Returns404()
    {
        await Assert.ThrowsAsync<AppNotFoundException>(
            () => _service.HandleCallbackAsync((PaymentGateway)99, IpnCallback));
    }

    [Fact]
    public async Task HandleCallback_RunsAsTheSystemActor()
    {
        SetupCallback(paymentId: null);

        await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.True(_currentUserAccessor.IsSystemActor);
    }

    // A payment of the invoice that a VNPay callback cannot take (recorded in cash)
    private Payment SetupPaymentOfAnotherMethod()
    {
        _payment = _invoice.RecordPayment(
            new PaymentDetails(3_000_000, PaymentMethod.Cash, DateTimeOffset.UtcNow, null, null, null, null),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(_payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_invoice.Id);
        return _payment;
    }

    [Theory]
    [InlineData(false)] // no reference at all
    [InlineData(true)] // a reference that is not one of our payments
    public async Task HandleCallback_UnknownPayment_AnswersPaymentNotFoundWithoutRecordingIt(bool withReference)
    {
        SetupCallback(withReference ? Guid.NewGuid() : null);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.PaymentNotFound), response.Body);
        Assert.Empty(_transactions);
        _gateway.Verify(g => g.VerifyCallback(It.IsAny<GatewayCallback>(), It.IsAny<GatewayCredentials>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallback_PaidForAPaymentThatCannotTakeIt_RecordsItForReview()
    {
        var payment = SetupPaymentOfAnotherMethod();
        SetupCallback(payment.Id);
        _gateway.Setup(g => g.VerifyCallback(IpnCallback, It.IsAny<GatewayCredentials>())).Returns(true);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.PaymentNotFound), response.Body);
        var transaction = Assert.Single(_transactions);
        Assert.Equal((PaymentTransactionStatus.Failed, payment.Id), (transaction.Status, transaction.PaymentId));
        Assert.True(transaction.NeedsReview);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task HandleCallback_UnsignedForAPaymentThatCannotTakeIt_IsRecordedWithoutReview()
    {
        var payment = SetupPaymentOfAnotherMethod();
        SetupCallback(payment.Id);
        _gateway.Setup(g => g.VerifyCallback(IpnCallback, It.IsAny<GatewayCredentials>())).Returns(false);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.InvalidSignature), response.Body);
        var transaction = Assert.Single(_transactions);
        Assert.Equal((PaymentTransactionStatus.Failed, payment.Id, false), (transaction.Status, transaction.PaymentId, transaction.NeedsReview));
    }

    [Fact]
    public async Task HandleCallback_SuspiciousDebit_VoidsThePaymentAndFlagsTheDebit()
    {
        var payment = SetupReturnFor(visible: true);
        _gateway.Setup(g => g.ParseCallback(IpnCallback))
            .Returns(new GatewayCallbackData(payment.Id, "14123456", 3_000_000m, IsSuccess: false, "{}", IsSuspicious: true));
        _gateway
            .Setup(g => g.BuildCallbackResponse(It.IsAny<GatewayCallbackOutcome>()))
            .Returns((GatewayCallbackOutcome outcome) => new GatewayHttpResponse(200, "application/json", outcome.ToString()));
        _gateway.Setup(g => g.VerifyCallback(IpnCallback, It.IsAny<GatewayCredentials>())).Returns(true);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.Received), response.Body);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        var transaction = Assert.Single(_transactions);
        Assert.Equal(
            (PaymentTransactionStatus.Failed, true, "14123456"),
            (transaction.Status, transaction.NeedsReview, transaction.GatewayTransactionId));
        Assert.NotNull(transaction.Error);
    }

    private static readonly GatewayCredentials Version1 = new("TESTCODE", "TESTSECRET");
    private static readonly GatewayCredentials Version2 = new("TESTCODE", "SECRET2");
    private static readonly GatewayCredentials Version3 = new("NEWCODE", "SECRET3");

    // A pending VNPay payment whose link was signed with credentials version 'linkVersion' (null: a checkout row
    // written before versions were recorded), an account changed twice since (now at version 3), and a callback
    // signed with 'signedWith'
    private Payment SetupChangedCredentials(int? linkVersion, GatewayCredentials signedWith)
    {
        var payment = SetupReturnFor(visible: true);
        _gateway.Setup(g => g.ParseCallback(IpnCallback))
            .Returns(new GatewayCallbackData(payment.Id, "14123456", 3_000_000m, IsSuccess: true, "{}"));
        _gateway
            .Setup(g => g.BuildCallbackResponse(It.IsAny<GatewayCallbackOutcome>()))
            .Returns((GatewayCallbackOutcome outcome) => new GatewayHttpResponse(200, "application/json", outcome.ToString()));
        _gateway
            .Setup(g => g.VerifyCallback(IpnCallback, It.IsAny<GatewayCredentials>()))
            .Returns((GatewayCallback _, GatewayCredentials c) => c == signedWith);

        var now = DateTimeOffset.UtcNow;
        var actorId = Guid.NewGuid();
        var account = OrganizationPaymentGateway.Create(
            _invoice.OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher", isActive: true, actorId, now.AddDays(-1));
        account.Update("TESTCODE", "cipher-2", isActive: true, actorId, now.AddMinutes(-3));
        account.Update("NEWCODE", "cipher-3", isActive: true, actorId, now.AddMinutes(-2));
        _secretProtector.Setup(p => p.Unprotect("cipher-2")).Returns("SECRET2");
        _secretProtector.Setup(p => p.Unprotect("cipher-3")).Returns("SECRET3");
        _organizationPaymentGatewayRepository
            .Setup(r => r.GetAsync(_invoice.OrganizationId, PaymentGateway.VnPay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _organizationPaymentGatewayRepository
            .Setup(r => r.GetReplacedCredentialsAsync(account.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int version, CancellationToken _) =>
                account.CredentialHistories.SingleOrDefault(h => h.Version == version));

        var checkout = PaymentTransaction.Checkout(
            payment.Id, PaymentGateway.VnPay, 3_000_000m, "https://pay.test/x", now.AddMinutes(10), "{}", null,
            linkVersion ?? 0, now.AddMinutes(-5));
        if (linkVersion is null)
        {
            TestEntities.Set(checkout, new { CredentialsVersion = (int?)null });
        }

        _paymentTransactionRepository
            .Setup(r => r.GetLatestCheckoutAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkout);
        return payment;
    }

    [Fact]
    public async Task HandleCallback_SignedWithTheCredentialsOfTheLinkReplacedTwiceSince_IsAccepted()
    {
        var payment = SetupChangedCredentials(linkVersion: 1, signedWith: Version1);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.Received), response.Body);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
    }

    [Fact]
    public async Task HandleCallback_SignedWithTheCurrentCredentials_IsAccepted()
    {
        var payment = SetupChangedCredentials(linkVersion: 1, signedWith: Version3);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.Received), response.Body);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
    }

    [Fact]
    public async Task HandleCallback_SignedWithReplacedCredentialsOtherThanTheLinks_IsRejected()
    {
        var payment = SetupChangedCredentials(linkVersion: 2, signedWith: Version1);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.InvalidSignature), response.Body);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task HandleCallback_CheckoutWithoutCredentialsVersion_OnlyAcceptsTheCurrentOnes()
    {
        var payment = SetupChangedCredentials(linkVersion: null, signedWith: Version2);

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.InvalidSignature), response.Body);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        _organizationPaymentGatewayRepository.Verify(
            r => r.GetReplacedCredentialsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallback_UnexpectedError_DiscardsAndAnswersError()
    {
        var paymentId = Guid.NewGuid();
        SetupCallback(paymentId);
        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.Error), response.Body);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task HandleCallback_SameTransactionCommittedByAnotherDelivery_AnswersAlreadyProcessed()
    {
        SetupCallback(SetupPaymentOfAnotherMethod().Id);
        _paymentTransactionRepository
            .Setup(r => r.AddAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException(
                UniqueIndexNames.PaymentTransactionGatewayGatewayTransactionId, new Exception()));

        var response = await _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback);

        Assert.Equal(nameof(GatewayCallbackOutcome.AlreadyProcessed), response.Body);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task HandleCallback_Cancelled_Rethrows()
    {
        var paymentId = Guid.NewGuid();
        SetupCallback(paymentId);
        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.HandleCallbackAsync(PaymentGateway.VnPay, IpnCallback));
    }

    private static OnlinePaymentReturnRequest ReturnRequest() =>
        new() { Params = new Dictionary<string, string> { ["vnp_TxnRef"] = "x", ["invoiceId"] = Guid.NewGuid().ToString() } };

    private Payment SetupReturnFor(bool visible)
    {
        var payment = _invoice.RecordPayment(
            new PaymentDetails(3_000_000, PaymentMethod.VnPay, DateTimeOffset.UtcNow, null, null, null, null),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        _gateway
            .Setup(g => g.ParseCallback(It.IsAny<GatewayCallback>()))
            .Returns(new GatewayCallbackData(payment.Id, "14123456", 3_000_000m, IsSuccess: true, "{}"));
        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_invoice.Id);
        _paymentRepository
            .Setup(r => r.GetByIdWithDetailsAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        _tenantPortalQueries
            .Setup(q => q.IsInvoiceVisibleAsync(_currentUserAccessor.User!.Id, _invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(visible);
        _payment = payment; // what GetForUpdateAsync hands back under the lock
        return payment;
    }

    [Fact]
    public async Task HandleMyReturn_UnsupportedGateway_Returns404()
    {
        await Assert.ThrowsAsync<AppNotFoundException>(
            () => _service.HandleMyReturnAsync((PaymentGateway)99, ReturnRequest()));
    }

    [Fact]
    public async Task HandleMyReturn_NoPaymentReference_Returns404()
    {
        _gateway
            .Setup(g => g.ParseCallback(It.IsAny<GatewayCallback>()))
            .Returns(new GatewayCallbackData(null, null, null, IsSuccess: false, "{}"));

        await Assert.ThrowsAsync<AppNotFoundException>(() => _service.HandleMyReturnAsync(PaymentGateway.VnPay, ReturnRequest()));

        Assert.Empty(_transactions);
    }

    [Fact]
    public async Task HandleMyReturn_PaymentOfSomeoneElsesInvoice_Returns404BeforeWritingAnything()
    {
        SetupReturnFor(visible: false);

        await Assert.ThrowsAsync<AppNotFoundException>(() => _service.HandleMyReturnAsync(PaymentGateway.VnPay, ReturnRequest()));

        Assert.Empty(_transactions);
        Assert.False(_currentUserAccessor.IsSystemActor);
        _gateway.Verify(g => g.VerifyCallback(It.IsAny<GatewayCallback>(), It.IsAny<GatewayCredentials>()), Times.Never);
    }

    [Fact]
    public async Task HandleMyReturn_GoesThroughTheIpnChecksAndRecordsAReturn()
    {
        var payment = SetupReturnFor(visible: true);
        // Signature rejected: the shared checks ran, under the system actor, and the row says where it came from
        _gateway.Setup(g => g.VerifyCallback(It.IsAny<GatewayCallback>(), It.IsAny<GatewayCredentials>())).Returns(false);

        var response = await _service.HandleMyReturnAsync(PaymentGateway.VnPay, ReturnRequest());

        Assert.Equal((payment.Id, _invoice.Id), (response.PaymentId, response.InvoiceId));
        Assert.Equal((GatewayCallbackOutcome.InvalidSignature, PaymentStatus.Pending), (response.Outcome, response.PaymentStatus));
        var transaction = Assert.Single(_transactions);
        Assert.Equal((PaymentTransactionType.Return, PaymentTransactionStatus.Failed), (transaction.Type, transaction.Status));
        Assert.True(_currentUserAccessor.IsSystemActor);
    }
}
