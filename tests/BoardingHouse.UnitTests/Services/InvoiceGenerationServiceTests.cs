using Moq;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class InvoiceGenerationServiceTests
{
    private static readonly BillingMonth June = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));

    private readonly Mock<IPropertyRepository> _propertyRepository = new();
    private readonly Mock<ILeaseRepository> _leaseRepository = new();
    private readonly Mock<IInvoiceGenerator> _invoiceGenerator = new();
    private readonly Mock<IOrganizationScopeAccessor> _organizationScopeAccessor = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly User _actor = TestEntities.New<User>(new
    {
        Email = "actor@test.com",
        PasswordHash = "hashed-password",
        FullName = "Actor",
        CreatedBy = SentinelActors.System
    });
    private readonly InvoiceGenerationService _service;

    public InvoiceGenerationServiceTests()
    {
        MapsterTestSupport.EnsureUserMappingRegistered();
        _currentUserAccessor.SetupGet(a => a.RequiredUser).Returns(_actor);

        _service = new InvoiceGenerationService(
            _propertyRepository.Object,
            _leaseRepository.Object,
            _invoiceGenerator.Object,
            _organizationScopeAccessor.Object,
            _currentUserAccessor.Object);
    }

    private static OrganizationScope Unrestricted() => new() { IsUnrestricted = true, OrganizationIds = new HashSet<Guid>() };

    private static OrganizationScope Restricted(params Guid[] organizationIds) =>
        new() { IsUnrestricted = false, OrganizationIds = organizationIds.ToHashSet() };

    private void SetupScope(string action, OrganizationScope scope) =>
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

    private static InvoiceGenerationRun NewRun(Guid organizationId, Guid? propertyId, Guid? triggeredBy = null) =>
        InvoiceGenerationRun.Record(
            organizationId,
            propertyId,
            June,
            triggeredBy ?? Guid.NewGuid(),
            new DateTimeOffset(2026, 6, 5, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 5, 3, 0, 2, TimeSpan.Zero),
            createdCount: 2,
            [
                new InvoiceGenerationSkip(
                    Guid.NewGuid(),
                    "HD-001",
                    "101",
                    [new InvoiceGenerationSkipReason("missingReading", "No Electricity reading has been recorded for 2026-06")])
            ]);

    private static GenerateInvoicesRequest Request() => new() { BillingMonth = June.FirstDay };

    private void SetupProperty(Guid propertyId, Guid organizationId) =>
        _propertyRepository
            .Setup(r => r.GetOrganizationIdAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organizationId);

    [Fact]
    public async Task GenerateForPropertyAsync_UnknownProperty_ThrowsNotFoundWithoutListing()
    {
        var propertyId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _service.GenerateForPropertyAsync(propertyId, Request()));

        Assert.Equal(NotFoundMessages.Property(propertyId), ex.Message);
        _leaseRepository.Verify(
            r => r.ListToBillByPropertyAsync(It.IsAny<Guid>(), It.IsAny<BillingMonth>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForPropertyAsync_PropertyOfAnotherOrganization_ThrowsNotFoundWithoutListing()
    {
        var propertyId = Guid.NewGuid();
        SetupProperty(propertyId, Guid.NewGuid());
        SetupScope(PermissionActions.Create, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _service.GenerateForPropertyAsync(propertyId, Request()));

        Assert.Equal(NotFoundMessages.Property(propertyId), ex.Message);
        _leaseRepository.Verify(
            r => r.ListToBillByPropertyAsync(It.IsAny<Guid>(), It.IsAny<BillingMonth>(), It.IsAny<CancellationToken>()), Times.Never);
        _invoiceGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateForPropertyAsync_GeneratesTheLeasesOfThePropertyAsTheCurrentUser()
    {
        var organizationId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        SetupProperty(propertyId, organizationId);
        SetupScope(PermissionActions.Create, Restricted(organizationId));
        var leases = new List<Lease> { TestLeases.Active(new DateOnly(2026, 5, 1)) };
        _leaseRepository
            .Setup(r => r.ListToBillByPropertyAsync(propertyId, June, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leases);
        var run = NewRun(organizationId, propertyId, _actor.Id);
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                organizationId, propertyId, June, It.IsAny<DateOnly>(), leases, _actor.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var response = await _service.GenerateForPropertyAsync(propertyId, Request());

        Assert.Equal(run.Id, response.Id);
        Assert.Equal((organizationId, propertyId, June.FirstDay), (response.OrganizationId, response.PropertyId, response.BillingMonth));
        Assert.Equal(_actor.Id, response.TriggeredBy);
        Assert.Equal((run.StartedAt, run.FinishedAt), (response.StartedAt, response.FinishedAt));
        Assert.Equal((2, 1), (response.CreatedCount, response.SkippedCount));
        var skip = Assert.Single(response.Skipped);
        Assert.Equal((run.Skipped[0].LeaseId, "HD-001", "101"), (skip.LeaseId, skip.LeaseCode, skip.RoomNumber));
        var reason = Assert.Single(skip.Reasons);
        Assert.Equal(("missingReading", "No Electricity reading has been recorded for 2026-06"), (reason.Code, reason.Message));
    }

    // "Today" (due dates, proration) is the UTC date, whatever month is billed
    [Fact]
    public async Task GenerateForPropertyAsync_PassesTheUtcDateAsToday()
    {
        var organizationId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        SetupProperty(propertyId, organizationId);
        SetupScope(PermissionActions.Create, Unrestricted());
        _leaseRepository
            .Setup(r => r.ListToBillByPropertyAsync(propertyId, June, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<BillingMonth>(), It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<Lease>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewRun(organizationId, propertyId));

        await _service.GenerateForPropertyAsync(propertyId, Request());

        _invoiceGenerator.Verify(g => g.GenerateAllAsync(
            organizationId, propertyId, June, DateOnly.FromDateTime(DateTime.UtcNow),
            It.IsAny<IReadOnlyList<Lease>>(), _actor.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The run is recorded even when nothing is due: staff see that the click did happen
    [Fact]
    public async Task GenerateForPropertyAsync_NoLeasesToBill_StillGeneratesARun()
    {
        var organizationId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        SetupProperty(propertyId, organizationId);
        SetupScope(PermissionActions.Create, Unrestricted());
        _leaseRepository
            .Setup(r => r.ListToBillByPropertyAsync(propertyId, June, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _invoiceGenerator
            .Setup(g => g.GenerateAllAsync(
                organizationId, propertyId, June, It.IsAny<DateOnly>(),
                It.Is<IReadOnlyList<Lease>>(l => l.Count == 0), _actor.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewRun(organizationId, propertyId));

        var response = await _service.GenerateForPropertyAsync(propertyId, Request());

        Assert.Equal(organizationId, response.OrganizationId);
    }
}
