using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Jobs;

public class VoidExpiredOnlinePaymentsJobTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CurrentUserAccessor _currentUserAccessor = new();
    private readonly VoidExpiredOnlinePaymentsJob _job;

    public VoidExpiredOnlinePaymentsJobTests()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken ct) => operation(ct));

        _job = new VoidExpiredOnlinePaymentsJob(
            _paymentRepository.Object,
            _invoiceRepository.Object,
            _unitOfWork.Object,
            _currentUserAccessor,
            NullLogger<VoidExpiredOnlinePaymentsJob>.Instance);
    }

    // A 3,000,000 invoice with a pending VNPay payment of the whole amount
    private Payment SetupPendingPayment()
    {
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        IReadOnlyList<InvoiceLine> lines = [new("Rent", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, Guid.NewGuid())];
        var invoice = Invoice.Create(
            new InvoiceCalculation
            {
                BillingMonth = BillingMonth.Of(dueDate),
                DueDate = dueDate,
                VatRate = 0,
                DiscountAmount = 0,
                Lines = lines,
                Totals = InvoiceTotals.Compute(lines.Select(l => (l.SourceType, l.Amount)), 0, 0, 0),
                Errors = [],
                Warnings = []
            },
            Guid.NewGuid(), Guid.NewGuid(), "INV-TEST-0001",
            discountReason: null, notes: null, DateTimeOffset.UtcNow, SentinelActors.System);

        var now = DateTimeOffset.UtcNow;
        var payment = invoice.RecordPayment(
            new PaymentDetails(3_000_000, PaymentMethod.VnPay, now, Guid.NewGuid(), null, null, null),
            Guid.NewGuid(),
            now);

        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice.Id);
        _paymentRepository
            .Setup(r => r.GetForUpdateAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        return payment;
    }

    private void SetupStalePaymentIds(params Guid[] ids) =>
        _paymentRepository
            .Setup(r => r.ListStaleGatewayPaymentIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.ToList());

    [Fact]
    public async Task RunAsync_StalePendingPayment_VoidsItAsTheSystem()
    {
        var payment = SetupPendingPayment();
        SetupStalePaymentIds(payment.Id);

        await _job.RunAsync();

        Assert.Equal((PaymentStatus.Voided, Payment.GatewayExpiredReason), (payment.Status, payment.VoidReason));
        Assert.Null(payment.VoidedBy);
        Assert.True(_currentUserAccessor.IsSystemActor);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ListsPaymentsCreatedBeforeTheLinkLifetimePlusTheGrace()
    {
        DateTimeOffset? createdBefore = null;
        _paymentRepository
            .Setup(r => r.ListStaleGatewayPaymentIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback((DateTimeOffset value, CancellationToken _) => createdBefore = value)
            .ReturnsAsync([]);
        var before = DateTimeOffset.UtcNow;

        await _job.RunAsync();

        var age = Payment.GatewayCheckoutLifetime + Payment.GatewayVoidGrace;
        Assert.InRange(createdBefore!.Value, before - age, DateTimeOffset.UtcNow - age);
    }

    [Fact]
    public async Task RunAsync_ConfirmedByAnIpnBeforeTheLock_Skips()
    {
        var payment = SetupPendingPayment();
        TestEntities.Set(payment, new { Status = PaymentStatus.Confirmed });
        SetupStalePaymentIds(payment.Id);

        await _job.RunAsync();

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
    }

    [Fact]
    public async Task RunAsync_PaymentGone_Skips()
    {
        SetupStalePaymentIds(Guid.NewGuid());

        await _job.RunAsync();

        _invoiceRepository.Verify(
            r => r.GetAggregateForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_OnePaymentFails_StillVoidsTheOthers()
    {
        var broken = Guid.NewGuid();
        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(broken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var payment = SetupPendingPayment();
        SetupStalePaymentIds(broken, payment.Id);

        await _job.RunAsync();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_NothingStale_OpensNoTransaction()
    {
        SetupStalePaymentIds();

        await _job.RunAsync();

        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_Cancelled_StopsBeforeTheNextPayment()
    {
        var payment = SetupPendingPayment();
        SetupStalePaymentIds(payment.Id);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _job.RunAsync(cts.Token));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }
}
