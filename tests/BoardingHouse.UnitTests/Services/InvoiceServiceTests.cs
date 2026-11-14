using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class InvoiceServiceTests
{
    private static readonly BillingMonth June = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));

    private readonly Mock<ILeaseRepository> _leaseRepository = new();
    private readonly Mock<IRoomRepository> _roomRepository = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOrganizationScopeAccessor> _organizationScopeAccessor = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly Mock<ICurrentOrganizationAccessor> _currentOrganizationAccessor = new();
    private readonly InvoiceService _invoiceService;

    private SearchCall? _search;

    private sealed record SearchCall(
        Guid? OrganizationId,
        IReadOnlySet<Guid>? AllowedOrganizationIds,
        Guid? PropertyId,
        Guid? RoomId,
        Guid? LeaseId,
        InvoiceStatus? Status,
        BillingMonth? BillingMonth,
        string? Search,
        PageRequest PageRequest,
        Expression<Func<Invoice, object>> SortField,
        SortOrder SortOrder);

    public InvoiceServiceTests()
    {
        MapsterTestSupport.EnsureUserMappingRegistered();

        var billingInputLoader = new InvoiceBillingInputLoader(
            _leaseRepository.Object,
            Mock.Of<IOrganizationSettingsRepository>(),
            Mock.Of<IUtilityReadingRepository>(),
            _invoiceRepository.Object);

        _invoiceService = new InvoiceService(
            _leaseRepository.Object,
            _roomRepository.Object,
            _invoiceRepository.Object,
            billingInputLoader,
            _unitOfWork.Object,
            _organizationScopeAccessor.Object,
            _currentUserAccessor.Object,
            _currentOrganizationAccessor.Object,
            NullLogger<InvoiceService>.Instance);

        SetupSearchReturns([]);
    }

    private void SetupScope(OrganizationScope scope) =>
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

    private static OrganizationScope Unrestricted() => new() { IsUnrestricted = true, OrganizationIds = new HashSet<Guid>() };

    private static OrganizationScope Restricted(params Guid[] organizationIds) =>
        new() { IsUnrestricted = false, OrganizationIds = organizationIds.ToHashSet() };

    private void SetupSearchReturns(List<Invoice> items, int page = 1, int pageSize = 20, int? totalItems = null) =>
        _invoiceRepository
            .Setup(r => r.SearchAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<InvoiceStatus?>(), It.IsAny<BillingMonth?>(), It.IsAny<string?>(), It.IsAny<PageRequest>(),
                It.IsAny<Expression<Func<Invoice, object>>>(), It.IsAny<SortOrder>(), It.IsAny<CancellationToken>()))
            .Callback((Guid? organizationId, IReadOnlySet<Guid>? allowed, Guid? propertyId, Guid? roomId, Guid? leaseId,
                    InvoiceStatus? status, BillingMonth? month, string? search, PageRequest pageRequest,
                    Expression<Func<Invoice, object>> sortField, SortOrder sortOrder, CancellationToken _) =>
                _search = new SearchCall(organizationId, allowed, propertyId, roomId, leaseId, status, month, search,
                    pageRequest, sortField, sortOrder))
            .ReturnsAsync(new PagedResult<Invoice>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems ?? items.Count
            });

    private static string SortMemberName(Expression<Func<Invoice, object>> sortField)
    {
        var body = sortField.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : sortField.Body;
        return ((MemberExpression)body).Member.Name;
    }

    private static Invoice NewInvoice(string invoiceNumber = "INV-202606-0001")
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101");
        TestEntities.Set(room, new { Property = property });
        var lease = TestLeases.Active(new DateOnly(2026, 5, 1), room: room);

        InvoiceLine[] lines = [new("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, Guid.NewGuid())];
        var calculation = new InvoiceCalculation
        {
            BillingMonth = June,
            DueDate = new DateOnly(2026, 6, 8),
            VatRate = 0,
            DiscountAmount = 0,
            Lines = lines,
            Totals = InvoiceTotals.Compute(lines.Select(l => (l.SourceType, l.Amount)), 0, 0, 0),
            Errors = [],
            Warnings = []
        };
        var invoice = Invoice.Create(
            calculation, property.OrganizationId, lease.Id, invoiceNumber,
            discountReason: null, notes: null, DateTimeOffset.UtcNow, SentinelActors.System);
        TestEntities.Set(invoice, new { Lease = lease });
        return invoice;
    }

    [Fact]
    public async Task GetAllAsync_UnrestrictedWithoutOrganization_SearchesEveryOrganization()
    {
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery());

        Assert.NotNull(_search);
        Assert.Null(_search.OrganizationId);
        Assert.Null(_search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_RestrictedWithoutOrganization_SearchesOnlyAllowedOrganizations()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        SetupScope(Restricted(orgA, orgB));

        await _invoiceService.GetAllAsync(new InvoiceListQuery());

        Assert.NotNull(_search);
        Assert.Null(_search.OrganizationId);
        Assert.Equal(new HashSet<Guid> { orgA, orgB }, _search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_RestrictedWithoutMemberships_SearchesWithEmptyAllowedSet()
    {
        SetupScope(OrganizationScope.None);

        await _invoiceService.GetAllAsync(new InvoiceListQuery());

        Assert.NotNull(_search);
        Assert.NotNull(_search.AllowedOrganizationIds);
        Assert.Empty(_search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_AllowedOrganizationInQuery_SearchesThatOrganizationOnly()
    {
        var orgA = Guid.NewGuid();
        SetupScope(Restricted(orgA, Guid.NewGuid()));

        await _invoiceService.GetAllAsync(new InvoiceListQuery { OrganizationId = orgA });

        Assert.NotNull(_search);
        Assert.Equal(orgA, _search.OrganizationId);
        Assert.Null(_search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_UnrestrictedWithOrganizationInQuery_SearchesThatOrganization()
    {
        var orgId = Guid.NewGuid();
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery { OrganizationId = orgId });

        Assert.NotNull(_search);
        Assert.Equal(orgId, _search.OrganizationId);
        Assert.Null(_search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_OrganizationNotInScope_ThrowsNotFoundWithoutSearching()
    {
        var otherOrg = Guid.NewGuid();
        SetupScope(Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _invoiceService.GetAllAsync(new InvoiceListQuery { OrganizationId = otherOrg }));

        Assert.Equal(NotFoundMessages.Organization(otherOrg), ex.Message);
        Assert.Null(_search);
    }

    [Fact]
    public async Task GetAllAsync_NoOrganizationInQuery_FallsBackToCurrentOrganizationHeader()
    {
        var headerOrg = Guid.NewGuid();
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(headerOrg);
        SetupScope(Restricted(headerOrg));

        await _invoiceService.GetAllAsync(new InvoiceListQuery());

        Assert.NotNull(_search);
        Assert.Equal(headerOrg, _search.OrganizationId);
        Assert.Null(_search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_OrganizationInQuery_WinsOverCurrentOrganizationHeader()
    {
        var queryOrg = Guid.NewGuid();
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(Guid.NewGuid());
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery { OrganizationId = queryOrg });

        Assert.NotNull(_search);
        Assert.Equal(queryOrg, _search.OrganizationId);
    }

    [Fact]
    public async Task GetAllAsync_CurrentOrganizationHeaderNotInScope_ThrowsNotFound()
    {
        var headerOrg = Guid.NewGuid();
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(headerOrg);
        SetupScope(Restricted(Guid.NewGuid()));

        await Assert.ThrowsAsync<AppNotFoundException>(() => _invoiceService.GetAllAsync(new InvoiceListQuery()));
        Assert.Null(_search);
    }

    [Fact]
    public async Task GetAllAsync_PassesFiltersSearchAndPagingToRepository()
    {
        SetupScope(Unrestricted());
        var query = new InvoiceListQuery
        {
            PropertyId = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            LeaseId = Guid.NewGuid(),
            Status = InvoiceStatus.Paid,
            Search = "INV-2026",
            Page = 3,
            PageSize = 15,
            SortOrder = SortOrder.Asc
        };

        await _invoiceService.GetAllAsync(query);

        Assert.NotNull(_search);
        Assert.Equal(query.PropertyId, _search.PropertyId);
        Assert.Equal(query.RoomId, _search.RoomId);
        Assert.Equal(query.LeaseId, _search.LeaseId);
        Assert.Equal(InvoiceStatus.Paid, _search.Status);
        Assert.Equal("INV-2026", _search.Search);
        Assert.Equal(3, _search.PageRequest.Page);
        Assert.Equal(15, _search.PageRequest.PageSize);
        Assert.Equal(SortOrder.Asc, _search.SortOrder);
    }

    [Fact]
    public async Task GetAllAsync_WithoutFilters_PassesNullFilters()
    {
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery());

        Assert.NotNull(_search);
        Assert.Null(_search.PropertyId);
        Assert.Null(_search.RoomId);
        Assert.Null(_search.LeaseId);
        Assert.Null(_search.Status);
        Assert.Null(_search.BillingMonth);
        Assert.Null(_search.Search);
        Assert.Equal(SortOrder.Desc, _search.SortOrder);
    }

    [Fact]
    public async Task GetAllAsync_BillingMonth_ConvertsToBillingMonth()
    {
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery { BillingMonth = new DateOnly(2026, 6, 1) });

        Assert.NotNull(_search);
        Assert.Equal(June, _search.BillingMonth);
    }

    [Theory]
    [InlineData(null, nameof(Invoice.BillingMonth))]
    [InlineData("", nameof(Invoice.BillingMonth))]
    [InlineData("unknownField", nameof(Invoice.BillingMonth))]
    [InlineData("billingMonth", nameof(Invoice.BillingMonth))]
    [InlineData("dueDate", nameof(Invoice.DueDate))]
    [InlineData("createdAt", nameof(Invoice.CreatedAt))]
    [InlineData("invoiceNumber", nameof(Invoice.InvoiceNumber))]
    [InlineData("totalAmount", nameof(Invoice.TotalAmount))]
    [InlineData("TOTALAMOUNT", nameof(Invoice.TotalAmount))]
    public async Task GetAllAsync_SortBy_ResolvesSortField(string? sortBy, string expectedMember)
    {
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery { SortBy = sortBy });

        Assert.NotNull(_search);
        Assert.Equal(expectedMember, SortMemberName(_search.SortField));
    }

    [Fact]
    public async Task GetAllAsync_MapsItemsAndKeepsPaging()
    {
        SetupScope(Unrestricted());
        var first = NewInvoice("INV-202606-0001");
        var second = NewInvoice("INV-202606-0002");
        SetupSearchReturns([first, second], page: 2, pageSize: 2, totalItems: 5);

        var result = await _invoiceService.GetAllAsync(new InvoiceListQuery { Page = 2, PageSize = 2 });

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalItems);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(["INV-202606-0001", "INV-202606-0002"], result.Items.Select(i => i.InvoiceNumber));
        Assert.Equal([first.Id, second.Id], result.Items.Select(i => i.Id));
        Assert.All(result.Items, i =>
        {
            Assert.Equal("Sunrise House", i.PropertyName);
            Assert.Equal("101", i.RoomNumber);
            Assert.Equal(June.FirstDay, i.BillingMonth);
            Assert.Equal(3_000_000, i.TotalAmount);
        });
    }

    [Fact]
    public async Task GetAllAsync_EmptyPage_ReturnsEmptyItems()
    {
        SetupScope(Unrestricted());

        var result = await _invoiceService.GetAllAsync(new InvoiceListQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task GetAllAsync_ChecksInvoiceReadScope()
    {
        SetupScope(Unrestricted());

        await _invoiceService.GetAllAsync(new InvoiceListQuery());

        _organizationScopeAccessor.Verify(
            a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Read, It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.VerifyNoOtherCalls();
    }
}
