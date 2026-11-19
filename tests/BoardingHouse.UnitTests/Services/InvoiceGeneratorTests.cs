using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class InvoiceGeneratorTests
{
    private static readonly BillingMonth June = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));
    private static readonly DateOnly Today = new(2026, 6, 5);

    private readonly Mock<ILeaseRepository> _leaseRepository = new();
    private readonly Mock<IRoomRepository> _roomRepository = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IInvoiceGenerationRunRepository> _runRepository = new();
    private readonly Mock<IUtilityReadingRepository> _utilityReadingRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<Invoice> _addedInvoices = [];
    private readonly List<InvoiceGenerationRun> _addedRuns = [];
    private readonly InvoiceGenerator _generator;

    public InvoiceGeneratorTests()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<InvoiceGenerationResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<InvoiceGenerationResult>> operation, CancellationToken ct) => operation(ct));
        _invoiceRepository
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice invoice, CancellationToken _) => _addedInvoices.Add(invoice))
            .Returns(Task.CompletedTask);
        _invoiceRepository
            .Setup(r => r.NextNumberAsync(It.IsAny<Guid>(), June, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        _runRepository
            .Setup(r => r.AddAsync(It.IsAny<InvoiceGenerationRun>(), It.IsAny<CancellationToken>()))
            .Callback((InvoiceGenerationRun run, CancellationToken _) => _addedRuns.Add(run))
            .Returns(Task.CompletedTask);

        // No services, no readings: a lease bills its rent only
        _leaseRepository
            .Setup(r => r.ListUtilityServicesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaseUtilityService>());
        _utilityReadingRepository
            .Setup(r => r.ListByRoomAndMonthWithImageAsync(It.IsAny<Guid>(), It.IsAny<BillingMonth>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityWithMedia<UtilityReading>>());
        _utilityReadingRepository
            .Setup(r => r.ListUninvoicedByBilledLeaseIdAsync(It.IsAny<Guid>(), It.IsAny<BillingMonth>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UtilityReading>());

        var billingInputLoader = new InvoiceBillingInputLoader(
            _leaseRepository.Object,
            Mock.Of<IOrganizationSettingsRepository>(),
            _utilityReadingRepository.Object,
            _invoiceRepository.Object);

        _generator = new InvoiceGenerator(
            _leaseRepository.Object,
            _roomRepository.Object,
            _invoiceRepository.Object,
            _runRepository.Object,
            billingInputLoader,
            _unitOfWork.Object,
            NullLogger<InvoiceGenerator>.Instance);
    }

    // Active since May, rent 3,000,000, billing day = start day (1): June is due on Today
    private Lease SetupLease(string roomNumber = "101", bool draft = false)
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: roomNumber);
        TestEntities.Set(room, new { Property = property });
        var lease = draft
            ? TestLeases.Draft(new DateOnly(2026, 5, 1), room: room)
            : TestLeases.Active(new DateOnly(2026, 5, 1), room: room);

        _leaseRepository
            .Setup(r => r.GetRoomIdAsync(lease.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(room.Id);
        _roomRepository
            .Setup(r => r.GetByIdWithOrganizationIdForShareAsync(room.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityWithOrganizationId<Room>(room, property.OrganizationId));
        _leaseRepository
            .Setup(r => r.GetByIdForBillingAsync(lease.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lease);

        return lease;
    }

    [Fact]
    public async Task CalculateAsync_AppliesTheDraftWithoutLockingNumberingOrAdding()
    {
        var lease = SetupLease();
        var draft = new InvoiceDraft(lease.Id, June, Today, [InvoiceLine.Custom("Faucet repair", 1, 50_000)], 10_000, null, null);

        var calculation = await _generator.CalculateAsync(lease, draft);

        Assert.True(calculation.CanCreate);
        Assert.Equal(June, calculation.BillingMonth);
        Assert.Equal(3_040_000, calculation.Totals.TotalAmount);
        Assert.Contains(calculation.Lines, l => l.Description == "Faucet repair");
        Assert.Empty(_addedInvoices);
        _invoiceRepository.Verify(
            r => r.NextNumberAsync(It.IsAny<Guid>(), It.IsAny<BillingMonth>(), It.IsAny<CancellationToken>()), Times.Never);
        _leaseRepository.Verify(
            r => r.GetByIdForBillingAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_BillableLease_CreatesTheNumberedInvoiceAsTheActor()
    {
        var lease = SetupLease();
        var actorId = Guid.NewGuid();

        var result = await _generator.GenerateAsync(InvoiceDraft.Monthly(lease.Id, June, Today), actorId);

        var created = Assert.IsType<InvoiceGenerationResult.Created>(result);
        var invoice = Assert.Single(_addedInvoices);
        Assert.Equal(invoice.Id, created.InvoiceId);
        Assert.Equal((lease.Id, lease.OrganizationId), (invoice.LeaseId, invoice.OrganizationId));
        Assert.Equal("INV-202606-0007", invoice.InvoiceNumber);
        Assert.Equal(June, invoice.BillingMonth);
        Assert.Equal(Today, invoice.DueDate);
        Assert.Equal(3_000_000, invoice.TotalAmount);
        Assert.Equal(actorId, invoice.CreatedBy);
        Assert.Equal(actorId, Assert.Single(invoice.StatusHistories).ChangedBy);
    }

    [Fact]
    public async Task GenerateAsync_DraftExtras_AreOnTheInvoice()
    {
        var lease = SetupLease();
        var draft = new InvoiceDraft(
            lease.Id, June, Today, [InvoiceLine.Custom("Faucet repair", 1, 50_000)], 10_000, "Loyal tenant", "Pay by transfer");

        await _generator.GenerateAsync(draft, Guid.NewGuid());

        var invoice = Assert.Single(_addedInvoices);
        Assert.Equal(3_040_000, invoice.TotalAmount);
        Assert.Equal(("Loyal tenant", "Pay by transfer"), (invoice.DiscountReason, invoice.Notes));
        Assert.Contains(invoice.Items, i => i.Description == "Faucet repair");
    }

    [Fact]
    public async Task GenerateAsync_WithoutActor_LeavesCreatedByToTheAuditInterceptor()
    {
        var lease = SetupLease();

        await _generator.GenerateAsync(InvoiceDraft.Monthly(lease.Id, June, Today), createdBy: null);

        var invoice = Assert.Single(_addedInvoices);
        Assert.Equal(Guid.Empty, invoice.CreatedBy);
        Assert.Null(Assert.Single(invoice.StatusHistories).ChangedBy);
    }

    [Fact]
    public async Task GenerateAsync_CalculationErrors_SkipsWithoutNumberingOrAdding()
    {
        var lease = SetupLease(draft: true);

        var result = await _generator.GenerateAsync(InvoiceDraft.Monthly(lease.Id, June, Today), Guid.NewGuid());

        var skipped = Assert.IsType<InvoiceGenerationResult.Skipped>(result);
        Assert.Equal(InvoiceIssueCode.LeaseNotBillable, Assert.Single(skipped.Errors).Code);
        Assert.False(skipped.AlreadyInvoiced);
        Assert.Empty(_addedInvoices);
        _invoiceRepository.Verify(
            r => r.NextNumberAsync(It.IsAny<Guid>(), It.IsAny<BillingMonth>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_LeaseAlreadyInvoiced_SkipsAsAlreadyInvoiced()
    {
        var lease = SetupLease();
        _invoiceRepository
            .Setup(r => r.ExistsLiveForLeaseMonthAsync(lease.Id, June, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _generator.GenerateAsync(InvoiceDraft.Monthly(lease.Id, June, Today), Guid.NewGuid());

        Assert.True(Assert.IsType<InvoiceGenerationResult.Skipped>(result).AlreadyInvoiced);
        Assert.Empty(_addedInvoices);
    }

    [Fact]
    public async Task GenerateAsync_UnknownLease_ThrowsNotFound()
    {
        var leaseId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _generator.GenerateAsync(InvoiceDraft.Monthly(leaseId, June, Today), Guid.NewGuid()));

        Assert.Equal(NotFoundMessages.Lease(leaseId), ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_RoomGoneBeforeTheLock_ThrowsNotFound()
    {
        var lease = SetupLease();
        _roomRepository
            .Setup(r => r.GetByIdWithOrganizationIdForShareAsync(lease.RoomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityWithOrganizationId<Room>?)null);

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _generator.GenerateAsync(InvoiceDraft.Monthly(lease.Id, June, Today), Guid.NewGuid()));

        Assert.Equal(NotFoundMessages.Lease(lease.Id), ex.Message);
        Assert.Empty(_addedInvoices);
    }

    [Fact]
    public async Task GenerateAsync_LeaseMovedToAnotherRoom_ThrowsConflict()
    {
        var lease = SetupLease();
        var otherRoom = TestRooms.New(roomNumber: "102");
        _leaseRepository
            .Setup(r => r.GetRoomIdAsync(lease.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherRoom.Id);
        _roomRepository
            .Setup(r => r.GetByIdWithOrganizationIdForShareAsync(otherRoom.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityWithOrganizationId<Room>(otherRoom, lease.OrganizationId));

        await Assert.ThrowsAsync<AppConflictException>(
            () => _generator.GenerateAsync(InvoiceDraft.Monthly(lease.Id, June, Today), Guid.NewGuid()));
        Assert.Empty(_addedInvoices);
    }

    [Fact]
    public async Task GenerateAsync_ConcurrentInsertOfTheMonth_SkipsAsAlreadyInvoicedAndDiscardsChanges()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<InvoiceGenerationResult>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException(UniqueIndexNames.InvoiceLeaseIdBillingMonth, new Exception()));

        var result = await _generator.GenerateAsync(InvoiceDraft.Monthly(Guid.NewGuid(), June, Today), Guid.NewGuid());

        var skipped = Assert.IsType<InvoiceGenerationResult.Skipped>(result);
        Assert.True(skipped.AlreadyInvoiced);
        Assert.Equal("The lease already has an invoice for 2026-06", Assert.Single(skipped.Errors).Message);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_OtherUniqueViolation_Propagates()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<InvoiceGenerationResult>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException(UniqueIndexNames.InvoiceOrganizationIdInvoiceNumber, new Exception()));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => _generator.GenerateAsync(InvoiceDraft.Monthly(Guid.NewGuid(), June, Today), Guid.NewGuid()));
    }

    [Fact]
    public async Task GenerateAllAsync_RecordsTheCreatedAndSkippedLeasesInOneRun()
    {
        var billable = SetupLease("101");
        var draft = SetupLease("102", draft: true);
        var missing = SetupLease("103");
        _leaseRepository
            .Setup(r => r.GetRoomIdAsync(missing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        var organizationId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var run = await _generator.GenerateAllAsync(
            organizationId, propertyId, June, Today, [billable, draft, missing], actorId);

        Assert.Equal(billable.Id, Assert.Single(_addedInvoices).LeaseId);
        Assert.Equal(1, run.CreatedCount);
        Assert.Equal((organizationId, propertyId, June, actorId), (run.OrganizationId, run.PropertyId, run.BillingMonth, run.TriggeredBy));
        Assert.True(run.FinishedAt >= run.StartedAt);
        Assert.Collection(
            run.Skipped,
            skip =>
            {
                Assert.Equal((draft.Id, "HD-TEST", "102"), (skip.LeaseId, skip.LeaseCode, skip.RoomNumber));
                Assert.Equal([new InvoiceGenerationSkipReason("leaseNotBillable", "A draft lease cannot be invoiced")], skip.Reasons);
            },
            skip =>
            {
                Assert.Equal((missing.Id, "103"), (skip.LeaseId, skip.RoomNumber));
                Assert.Equal([new InvoiceGenerationSkipReason(InvoiceIssueKeys.Failed, NotFoundMessages.Lease(missing.Id))], skip.Reasons);
            });
        Assert.Same(run, Assert.Single(_addedRuns));
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(3));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerateAllAsync_CreatesTheInvoicesAsTheTriggeringUserOrTheJob(bool byStaff)
    {
        var first = SetupLease("101");
        var second = SetupLease("102");
        Guid? triggeredBy = byStaff ? Guid.NewGuid() : null;

        var run = await _generator.GenerateAllAsync(Guid.NewGuid(), null, June, Today, [first, second], triggeredBy);

        Assert.Equal(2, run.CreatedCount);
        Assert.Equal([first.Id, second.Id], _addedInvoices.Select(i => i.LeaseId));
        Assert.All(_addedInvoices, i => Assert.Equal(triggeredBy ?? Guid.Empty, i.CreatedBy));
        Assert.All(_addedInvoices, i => Assert.Equal(triggeredBy, Assert.Single(i.StatusHistories).ChangedBy));
    }

    [Fact]
    public async Task GenerateAllAsync_LeaseInvoicedMeanwhile_IsNeitherCreatedNorSkipped()
    {
        var lease = SetupLease();
        _invoiceRepository
            .Setup(r => r.ExistsLiveForLeaseMonthAsync(lease.Id, June, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var run = await _generator.GenerateAllAsync(Guid.NewGuid(), null, June, Today, [lease], triggeredBy: null);

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Same(run, Assert.Single(_addedRuns));
    }

    [Fact]
    public async Task GenerateAllAsync_UnexpectedError_IsSkippedWithAGenericMessageAndTheRestContinues()
    {
        var failing = SetupLease("101");
        var billable = SetupLease("102");
        _leaseRepository
            .Setup(r => r.GetRoomIdAsync(failing.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection reset"));

        var run = await _generator.GenerateAllAsync(Guid.NewGuid(), null, June, Today, [failing, billable], triggeredBy: null);

        Assert.Equal(1, run.CreatedCount);
        var skip = Assert.Single(run.Skipped);
        Assert.Equal(failing.Id, skip.LeaseId);
        Assert.Equal([new InvoiceGenerationSkipReason(InvoiceIssueKeys.Failed, InvoiceGenerator.UnexpectedErrorMessage)], skip.Reasons);
    }

    [Fact]
    public async Task GenerateAllAsync_DomainError_KeepsItsMessage()
    {
        var lease = SetupLease();
        _leaseRepository
            .Setup(r => r.GetRoomIdAsync(lease.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Something the tenant can fix", DomainErrorKind.Conflict));

        var run = await _generator.GenerateAllAsync(Guid.NewGuid(), null, June, Today, [lease], triggeredBy: null);

        Assert.Equal(
            [new InvoiceGenerationSkipReason(InvoiceIssueKeys.Failed, "Something the tenant can fix")],
            Assert.Single(run.Skipped).Reasons);
    }

    [Fact]
    public async Task GenerateAllAsync_NoLeases_RecordsAnEmptyRun()
    {
        var run = await _generator.GenerateAllAsync(Guid.NewGuid(), Guid.NewGuid(), June, Today, [], Guid.NewGuid());

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Same(run, Assert.Single(_addedRuns));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAllAsync_CancelledBeforeStarting_RecordsNoRun()
    {
        var lease = SetupLease();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _generator.GenerateAllAsync(
            Guid.NewGuid(), null, June, Today, [lease], triggeredBy: null, cancellation.Token));

        Assert.Empty(_addedInvoices);
        Assert.Empty(_addedRuns);
    }

    // Cancellation is not a lease failure: it must stop the run, not be recorded as "failed"
    [Fact]
    public async Task GenerateAllAsync_CancelledWhileGenerating_PropagatesAndRecordsNoRun()
    {
        var lease = SetupLease();
        _leaseRepository
            .Setup(r => r.GetRoomIdAsync(lease.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _generator.GenerateAllAsync(
            Guid.NewGuid(), null, June, Today, [lease], triggeredBy: null));

        Assert.Empty(_addedRuns);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }
}
