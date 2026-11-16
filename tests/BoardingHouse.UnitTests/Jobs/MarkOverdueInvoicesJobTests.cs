using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Jobs;

public class MarkOverdueInvoicesJobTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IPropertyRepository> _propertyRepository = new();
    private readonly Mock<IOrganizationSettingsRepository> _organizationSettingsRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CurrentUserAccessor _currentUserAccessor = new();
    private readonly MarkOverdueInvoicesJob _job;

    public MarkOverdueInvoicesJobTests()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken ct) => operation(ct));

        _job = new MarkOverdueInvoicesJob(
            _invoiceRepository.Object,
            new InvoiceLateFeeResolver(_propertyRepository.Object, _organizationSettingsRepository.Object),
            _unitOfWork.Object,
            _currentUserAccessor,
            NullLogger<MarkOverdueInvoicesJob>.Instance);
    }

    // Rent 3,000,000, no VAT
    private Invoice SetupInvoice(DateOnly dueDate, InvoiceStatus status = InvoiceStatus.Pending, decimal paidAmount = 0)
    {
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
        var invoice = Invoice.Create(
            calculation, Guid.NewGuid(), Guid.NewGuid(), "INV-TEST-0001",
            discountReason: null, notes: null, DateTimeOffset.UtcNow, SentinelActors.System);
        TestEntities.Set(invoice, new { Status = status, PaidAmount = paidAmount });

        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        return invoice;
    }

    private void SetupInvoiceIdsToMarkOverdue(params Guid[] ids) =>
        _invoiceRepository
            .Setup(r => r.ListIdsToMarkOverdueAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.ToList());

    private void SetupPropertyLateFee(Invoice invoice, LateFeeType type, decimal value) =>
        _propertyRepository
            .Setup(r => r.GetByLeaseIdAsync(invoice.LeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestEntities.New<Property>(new
            {
                Name = "Sunrise House",
                LateFeeType = (LateFeeType?)type,
                LateFeeValue = (decimal?)value,
                CreatedBy = SentinelActors.System
            }));

    private void SetupOrganizationLateFee(Invoice invoice, LateFeeType type, decimal value)
    {
        var settings = OrganizationSettings.DefaultsFor(invoice.OrganizationId);
        TestEntities.Set(settings, new { LateFeeType = (LateFeeType?)type, LateFeeValue = (decimal?)value });
        _organizationSettingsRepository
            .Setup(r => r.GetByOrganizationIdAsync(invoice.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
    }

    [Fact]
    public async Task RunAsync_PastDueInvoice_MarksOverdueWithThePropertyLateFeeAndSystemHistory()
    {
        var invoice = SetupInvoice(Today.AddDays(-1));
        SetupPropertyLateFee(invoice, LateFeeType.Fixed, 100_000);
        SetupOrganizationLateFee(invoice, LateFeeType.Percent, 5);
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await _job.RunAsync();

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(100_000, invoice.LateFeeAmount);
        Assert.Equal(3_100_000, invoice.TotalAmount);
        Assert.True(_currentUserAccessor.IsSystemActor);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(InvoiceStatus.Overdue, history.NewStatus);
        Assert.Null(history.ChangedBy);
        Assert.Equal(Invoice.OverdueReason, history.Reason);
    }

    [Fact]
    public async Task RunAsync_OrganizationLateFeeOnly_ChargesItsPercent()
    {
        var invoice = SetupInvoice(Today.AddDays(-1));
        SetupOrganizationLateFee(invoice, LateFeeType.Percent, 5);
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await _job.RunAsync();

        Assert.Equal(150_000, invoice.LateFeeAmount);
        Assert.Equal(3_150_000, invoice.TotalAmount);
    }

    [Fact]
    public async Task RunAsync_NoLateFeeConfigured_MarksOverdueWithoutAFee()
    {
        var invoice = SetupInvoice(Today.AddDays(-1));
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await _job.RunAsync();

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
    }

    [Fact]
    public async Task RunAsync_PartiallyPaidPastDueInvoice_MarksOverdueWithTheFeeOnTheWholeTotal()
    {
        var invoice = SetupInvoice(Today.AddDays(-1), InvoiceStatus.Partial, paidAmount: 1_000_000);
        SetupOrganizationLateFee(invoice, LateFeeType.Percent, 5);
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await _job.RunAsync();

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(150_000, invoice.LateFeeAmount);
        Assert.Equal(2_150_000, invoice.RemainingAmount);
        Assert.Equal(InvoiceStatus.Partial, invoice.StatusHistories.Last().OldStatus);
    }

    [Theory]
    [InlineData(InvoiceStatus.Pending, 0)]
    [InlineData(InvoiceStatus.Cancelled, -1)]
    [InlineData(InvoiceStatus.Overdue, -1)]
    public async Task RunAsync_InvoiceNoLongerDueUnderLock_Skips(InvoiceStatus status, int dueDateOffsetDays)
    {
        var invoice = SetupInvoice(Today.AddDays(dueDateOffsetDays), status);
        SetupOrganizationLateFee(invoice, LateFeeType.Fixed, 50_000);
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await _job.RunAsync();

        Assert.Equal(status, invoice.Status);
        Assert.Single(invoice.StatusHistories);
        Assert.Equal(0, invoice.LateFeeAmount);
    }

    [Fact]
    public async Task RunAsync_PaidInFullBeforeTheLock_BecomesPaidWithoutAFee()
    {
        // Listed as PARTIAL, but a payment completed it before the job took the lock
        var invoice = SetupInvoice(Today.AddDays(-1), InvoiceStatus.Partial, paidAmount: 3_000_000);
        SetupOrganizationLateFee(invoice, LateFeeType.Fixed, 50_000);
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await _job.RunAsync();

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
    }

    [Fact]
    public async Task RunAsync_DeletedInvoice_Skips()
    {
        SetupInvoiceIdsToMarkOverdue(Guid.NewGuid());

        await _job.RunAsync();

        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
        _propertyRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_NothingToMark_OpensNoTransaction()
    {
        SetupInvoiceIdsToMarkOverdue();

        await _job.RunAsync();

        _unitOfWork.VerifyNoOtherCalls();
        Assert.True(_currentUserAccessor.IsSystemActor);
    }

    [Fact]
    public async Task RunAsync_ListsWithTodaysUtcDate()
    {
        SetupInvoiceIdsToMarkOverdue();

        await _job.RunAsync();

        _invoiceRepository.Verify(
            r => r.ListIdsToMarkOverdueAsync(DateOnly.FromDateTime(DateTime.UtcNow), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_OneInvoiceFails_StillMarksTheOthersAndDiscardsChangesEachTime()
    {
        var failing = SetupInvoice(Today.AddDays(-1));
        var ok = SetupInvoice(Today.AddDays(-1));
        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(failing.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        SetupInvoiceIdsToMarkOverdue(failing.Id, ok.Id);

        await _job.RunAsync();

        Assert.Equal(InvoiceStatus.Pending, failing.Status);
        Assert.Equal(InvoiceStatus.Overdue, ok.Status);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_CommitFails_StillHandlesTheNextInvoice()
    {
        var first = SetupInvoice(Today.AddDays(-1));
        var second = SetupInvoice(Today.AddDays(-1));
        _unitOfWork
            .SetupSequence(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("commit failed"))
            .Returns(Task.FromResult(true));
        SetupInvoiceIdsToMarkOverdue(first.Id, second.Id);

        await _job.RunAsync();

        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_Cancelled_Throws()
    {
        var invoice = SetupInvoice(Today.AddDays(-1));
        SetupInvoiceIdsToMarkOverdue(invoice.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _job.RunAsync(new CancellationToken(canceled: true)));

        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileHandlingAnInvoice_StopsTheBatch()
    {
        var first = SetupInvoice(Today.AddDays(-1));
        var second = SetupInvoice(Today.AddDays(-1));
        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(first.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        SetupInvoiceIdsToMarkOverdue(first.Id, second.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _job.RunAsync());

        Assert.Equal(InvoiceStatus.Pending, second.Status);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }
}
