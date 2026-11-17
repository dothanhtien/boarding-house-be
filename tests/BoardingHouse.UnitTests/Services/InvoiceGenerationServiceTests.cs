using Moq;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
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
    private readonly Mock<IInvoiceGenerationRunRepository> _runRepository = new();
    private readonly Mock<IInvoiceGenerator> _invoiceGenerator = new();
    private readonly Mock<IOrganizationScopeAccessor> _organizationScopeAccessor = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly Mock<ICurrentOrganizationAccessor> _currentOrganizationAccessor = new();
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
            _runRepository.Object,
            _invoiceGenerator.Object,
            _organizationScopeAccessor.Object,
            _currentUserAccessor.Object,
            _currentOrganizationAccessor.Object);
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

    private sealed record SearchCall(
        Guid? OrganizationId, IReadOnlySet<Guid>? AllowedOrganizationIds, Guid? PropertyId, BillingMonth? BillingMonth);

    private SearchCall? _search;
    private PageRequest? _searchPage;

    private void SetupSearchReturns(params InvoiceGenerationRun[] runs) =>
        SetupSearchReturns(new PagedResult<InvoiceGenerationRun>
        {
            Items = runs.ToList(),
            Page = 1,
            PageSize = 20,
            TotalItems = runs.Length
        });

    private void SetupSearchReturns(PagedResult<InvoiceGenerationRun> result) =>
        _runRepository
            .Setup(r => r.SearchAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<BillingMonth?>(),
                It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .Callback((Guid? organizationId, IReadOnlySet<Guid>? allowed, Guid? propertyId, BillingMonth? month,
                    PageRequest page, CancellationToken _) =>
            {
                _search = new SearchCall(organizationId, allowed, propertyId, month);
                _searchPage = page;
            })
            .ReturnsAsync(result);

    [Fact]
    public async Task GetRunsAsync_RestrictedWithoutOrganization_SearchesOnlyAllowedOrganizations()
    {
        var allowed = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Restricted(allowed));
        SetupSearchReturns();

        await _service.GetRunsAsync(new InvoiceGenerationRunListQuery());

        Assert.Null(_search!.OrganizationId);
        Assert.Equal([allowed], _search.AllowedOrganizationIds!);
    }

    [Fact]
    public async Task GetRunsAsync_UnrestrictedWithoutOrganization_SearchesEveryOrganization()
    {
        SetupScope(PermissionActions.Read, Unrestricted());
        SetupSearchReturns();

        await _service.GetRunsAsync(new InvoiceGenerationRunListQuery());

        Assert.Equal(new SearchCall(null, null, null, null), _search);
    }

    [Fact]
    public async Task GetRunsAsync_UnrestrictedWithOrganization_SearchesThatOrganizationOnly()
    {
        var organizationId = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Unrestricted());
        SetupSearchReturns();

        await _service.GetRunsAsync(new InvoiceGenerationRunListQuery { OrganizationId = organizationId });

        Assert.Equal(new SearchCall(organizationId, null, null, null), _search);
    }

    [Fact]
    public async Task GetRunsAsync_OrganizationNotInScope_ThrowsNotFoundWithoutSearching()
    {
        var organizationId = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _service.GetRunsAsync(new InvoiceGenerationRunListQuery { OrganizationId = organizationId }));

        Assert.Equal(NotFoundMessages.Organization(organizationId), ex.Message);
        Assert.Null(_search);
    }

    [Fact]
    public async Task GetRunsAsync_NoOrganizationInQuery_FallsBackToCurrentOrganizationHeader()
    {
        var organizationId = Guid.NewGuid();
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(organizationId);
        SetupScope(PermissionActions.Read, Restricted(organizationId));
        SetupSearchReturns();

        await _service.GetRunsAsync(new InvoiceGenerationRunListQuery());

        Assert.Equal(new SearchCall(organizationId, null, null, null), _search);
    }

    [Fact]
    public async Task GetRunsAsync_OrganizationInQuery_WinsOverTheHeader()
    {
        var queried = Guid.NewGuid();
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(Guid.NewGuid());
        SetupScope(PermissionActions.Read, Restricted(queried));
        SetupSearchReturns();

        await _service.GetRunsAsync(new InvoiceGenerationRunListQuery { OrganizationId = queried });

        Assert.Equal(queried, _search!.OrganizationId);
    }

    // The header names an organization outside the caller's scope: 404, not a silent fallback to every organization
    [Fact]
    public async Task GetRunsAsync_HeaderOrganizationNotInScope_ThrowsNotFound()
    {
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(Guid.NewGuid());
        SetupScope(PermissionActions.Read, Restricted(Guid.NewGuid()));

        await Assert.ThrowsAsync<AppNotFoundException>(() => _service.GetRunsAsync(new InvoiceGenerationRunListQuery()));

        Assert.Null(_search);
    }

    [Fact]
    public async Task GetRunsAsync_PassesFiltersAndMapsTheRuns()
    {
        var organizationId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Restricted(organizationId));
        var run = NewRun(organizationId, propertyId);
        SetupSearchReturns(run);

        var result = await _service.GetRunsAsync(new InvoiceGenerationRunListQuery
        {
            OrganizationId = organizationId,
            PropertyId = propertyId,
            BillingMonth = June.FirstDay
        });

        Assert.Equal(new SearchCall(organizationId, null, propertyId, June), _search);
        var item = Assert.Single(result.Items);
        Assert.Equal((run.Id, June.FirstDay, 1), (item.Id, item.BillingMonth, item.SkippedCount));
        Assert.Equal("missingReading", Assert.Single(Assert.Single(item.Skipped).Reasons).Code);
    }

    [Fact]
    public async Task GetRunsAsync_PassesThePageAndKeepsThePagingOfTheResult()
    {
        SetupScope(PermissionActions.Read, Unrestricted());
        SetupSearchReturns(new PagedResult<InvoiceGenerationRun>
        {
            Items = [NewRun(Guid.NewGuid(), null)],
            Page = 3,
            PageSize = 1,
            TotalItems = 7
        });
        var query = new InvoiceGenerationRunListQuery { Page = 3, PageSize = 1 };

        var result = await _service.GetRunsAsync(query);

        Assert.Same(query, _searchPage);
        Assert.Equal((3, 1, 7), (result.Page, result.PageSize, result.TotalItems));
        Assert.Null(Assert.Single(result.Items).PropertyId);
    }
}
