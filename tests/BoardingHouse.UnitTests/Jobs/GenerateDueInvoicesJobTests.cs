using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Jobs;

public class GenerateDueInvoicesJobTests
{
    private static readonly DateOnly Today = new(2026, 6, 5);
    private static readonly BillingMonth June = BillingMonth.Of(Today);

    private readonly Mock<IOrganizationSettingsRepository> _organizationSettingsRepository = new();
    private readonly Mock<ILeaseRepository> _leaseRepository = new();
    private readonly Mock<IInvoiceGenerator> _invoiceGenerator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CurrentUserAccessor _currentUserAccessor = new();
    private readonly GenerateDueInvoicesJob _job;

    public GenerateDueInvoicesJobTests()
    {
        _job = new GenerateDueInvoicesJob(
            _organizationSettingsRepository.Object,
            _leaseRepository.Object,
            _invoiceGenerator.Object,
            _unitOfWork.Object,
            _currentUserAccessor,
            NullLogger<GenerateDueInvoicesJob>.Instance);
    }

    private void SetupOrganizations(params Guid[] organizationIds) =>
        _organizationSettingsRepository
            .Setup(r => r.ListOrganizationIdsWithAutoGenerateInvoicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(organizationIds.ToList());

    private List<Lease> SetupDueLeases(Guid organizationId, int count)
    {
        var leases = Enumerable.Range(0, count).Select(_ => TestLeases.Active(new DateOnly(2026, 5, 1))).ToList();
        _leaseRepository
            .Setup(r => r.ListDueForBillingAsync(organizationId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leases);
        return leases;
    }

    private void SetupGenerateAllReturns(Guid organizationId, int createdCount) =>
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                organizationId, null, June, Today, It.IsAny<IReadOnlyList<Lease>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceGenerationRun.Record(
                organizationId, null, June, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, createdCount, []));

    private void VerifyGenerateAllNever() =>
        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<BillingMonth>(), It.IsAny<DateOnly>(),
            It.IsAny<IReadOnlyList<Lease>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task RunAsync_GeneratesTheDueLeasesOfEachOrganizationAsTheSystem()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        SetupOrganizations(first, second);
        var firstLeases = SetupDueLeases(first, 2);
        var secondLeases = SetupDueLeases(second, 1);
        SetupGenerateAllReturns(first, 2);
        SetupGenerateAllReturns(second, 1);

        await _job.RunAsync(Today);

        Assert.True(_currentUserAccessor.IsSystemActor);
        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            first, null, June, Today, firstLeases, null, It.IsAny<CancellationToken>()), Times.Once);
        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            second, null, June, Today, secondLeases, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The billing month is the month of today, whatever day it is
    [Theory]
    [InlineData("2026-06-01", "2026-06-01")]
    [InlineData("2026-06-30", "2026-06-01")]
    [InlineData("2026-12-31", "2026-12-01")]
    public async Task RunAsync_BillsTheMonthOfToday(string today, string expectedMonth)
    {
        var day = DateOnly.Parse(today);
        var organizationId = Guid.NewGuid();
        SetupOrganizations(organizationId);
        var leases = new List<Lease> { TestLeases.Active(new DateOnly(2026, 5, 1)) };
        _leaseRepository
            .Setup(r => r.ListDueForBillingAsync(organizationId, day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leases);
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<BillingMonth>(), It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<Lease>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceGenerationRun.Record(
                organizationId, null, BillingMonth.Of(day), null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, []));

        await _job.RunAsync(day);

        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            organizationId, null, BillingMonth.FromFirstDay(DateOnly.Parse(expectedMonth)), day, leases, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_OrganizationWithoutDueLeases_RecordsNoRun()
    {
        var organizationId = Guid.NewGuid();
        SetupOrganizations(organizationId);
        SetupDueLeases(organizationId, 0);

        await _job.RunAsync(Today);

        VerifyGenerateAllNever();
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_NoOrganizationOptedIn_ListsNoLeases()
    {
        SetupOrganizations();

        await _job.RunAsync(Today);

        _leaseRepository.Verify(
            r => r.ListDueForBillingAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(_currentUserAccessor.IsSystemActor);
    }

    [Fact]
    public async Task RunAsync_OneOrganizationFails_StillGeneratesTheOthers()
    {
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        SetupOrganizations(failing, healthy);
        SetupDueLeases(failing, 1);
        SetupDueLeases(healthy, 1);
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                failing, null, June, Today, It.IsAny<IReadOnlyList<Lease>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection reset"));
        SetupGenerateAllReturns(healthy, 1);

        await _job.RunAsync(Today);

        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            healthy, null, June, Today, It.IsAny<IReadOnlyList<Lease>>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(2));
    }

    // Listing the leases is inside the per-organization try too
    [Fact]
    public async Task RunAsync_ListingTheLeasesOfOneOrganizationFails_StillGeneratesTheOthers()
    {
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        SetupOrganizations(failing, healthy);
        _leaseRepository
            .Setup(r => r.ListDueForBillingAsync(failing, Today, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection reset"));
        SetupDueLeases(healthy, 1);
        SetupGenerateAllReturns(healthy, 1);

        await _job.RunAsync(Today);

        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            healthy, null, June, Today, It.IsAny<IReadOnlyList<Lease>>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Cancellation (shutdown) stops the job instead of being logged as an organization failure
    [Fact]
    public async Task RunAsync_Cancelled_StopsAtTheCurrentOrganization()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        SetupOrganizations(first, second);
        SetupDueLeases(first, 1);
        SetupDueLeases(second, 1);
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                first, null, June, Today, It.IsAny<IReadOnlyList<Lease>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _job.RunAsync(Today));

        _leaseRepository.Verify(
            r => r.ListDueForBillingAsync(second, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeStarting_ListsNoLeases()
    {
        SetupOrganizations(Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _job.RunAsync(Today, cancellation.Token));

        _leaseRepository.Verify(
            r => r.ListDueForBillingAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WithoutDate_UsesTodayUtc()
    {
        var organizationId = Guid.NewGuid();
        SetupOrganizations(organizationId);
        var before = DateOnly.FromDateTime(DateTime.UtcNow);

        await _job.RunAsync();

        var after = DateOnly.FromDateTime(DateTime.UtcNow);
        _leaseRepository.Verify(r => r.ListDueForBillingAsync(
            organizationId, It.Is<DateOnly>(d => d >= before && d <= after), It.IsAny<CancellationToken>()), Times.Once);
    }
}
