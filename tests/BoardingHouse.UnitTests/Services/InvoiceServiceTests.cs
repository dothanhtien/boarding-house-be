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
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<IInvoiceGenerator> _invoiceGenerator = new();
    private readonly Mock<IPropertyRepository> _propertyRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
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
        _currentUserAccessor.SetupGet(a => a.RequiredUser).Returns(_actor);
        _paymentRepository
            .Setup(r => r.ListPendingForUpdateByInvoiceIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        _invoiceService = new InvoiceService(
            _leaseRepository.Object,
            _invoiceRepository.Object,
            _paymentRepository.Object,
            new InvoiceLateFeeResolver(_propertyRepository.Object, Mock.Of<IOrganizationSettingsRepository>()),
            _invoiceGenerator.Object,
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

    private static Invoice NewInvoice(string invoiceNumber = "INV-202606-0001", params InvoiceLine[] extraLines)
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101");
        TestEntities.Set(room, new { Property = property });
        var lease = TestLeases.Active(new DateOnly(2026, 5, 1), room: room);

        InvoiceLine[] lines = [new("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, Guid.NewGuid()), .. extraLines];
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

    private Invoice SetupInvoiceForUpdate(OrganizationScope? scope = null, string action = PermissionActions.Update)
    {
        var invoice = NewInvoice("INV-202606-0001", InvoiceLine.Custom("Faucet repair", 1, 50_000));
        TestEntities.Set(invoice, new { DiscountAmount = 10_000m, DiscountReason = "Old reason", Notes = "Old note" });

        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct));
        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepository
            .Setup(r => r.GetByIdWithDetailsAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope ?? Unrestricted());

        return invoice;
    }

    [Fact]
    public async Task UpdateAsync_UnknownInvoice_ThrowsNotFound()
    {
        SetupInvoiceForUpdate();
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _invoiceService.UpdateAsync(id, new UpdateInvoiceRequest()));

        Assert.Equal(NotFoundMessages.Invoice(id), ex.Message);
        _invoiceRepository.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_InvoiceOfAnotherOrganization_ThrowsNotFoundAndLeavesItUnchanged()
    {
        var invoice = SetupInvoiceForUpdate(Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { Notes = "Hacked" }));

        Assert.Equal(NotFoundMessages.Invoice(invoice.Id), ex.Message);
        Assert.Equal("Old note", invoice.Notes);
    }

    [Fact]
    public async Task UpdateAsync_MemberOfTheInvoiceOrganization_Updates()
    {
        var invoice = SetupInvoiceForUpdate();
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Restricted(invoice.OrganizationId));

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { Notes = "New" });

        Assert.Equal("New", invoice.Notes);
    }

    [Fact]
    public async Task UpdateAsync_ChecksInvoiceUpdateScopeInsideTheTransaction()
    {
        var invoice = SetupInvoiceForUpdate();

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest());

        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
        _organizationScopeAccessor.Verify(
            a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Update, It.IsAny<CancellationToken>()), Times.Once);
        _invoiceRepository.Verify(r => r.GetAggregateForUpdateAsync(invoice.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_OmittedFields_KeepTheirCurrentValues()
    {
        var invoice = SetupInvoiceForUpdate();
        var dueDate = invoice.DueDate;

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { Notes = "New note" });

        Assert.Equal("New note", invoice.Notes);
        Assert.Equal(10_000, invoice.DiscountAmount);
        Assert.Equal("Old reason", invoice.DiscountReason);
        Assert.Equal(dueDate, invoice.DueDate);
        Assert.Equal(2, invoice.Items.Count);
    }

    [Fact]
    public async Task UpdateAsync_FieldsSentAsNull_ClearThem()
    {
        var invoice = SetupInvoiceForUpdate();

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest
        {
            DiscountAmount = 0m,
            DiscountReason = null,
            Notes = null
        });

        Assert.Equal(0, invoice.DiscountAmount);
        Assert.Null(invoice.DiscountReason);
        Assert.Null(invoice.Notes);
    }

    [Fact]
    public async Task UpdateAsync_EveryDetail_IsApplied()
    {
        var invoice = SetupInvoiceForUpdate();

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest
        {
            DiscountAmount = 20_000m,
            DiscountReason = "Regular tenant",
            Notes = "Pay by transfer",
            DueDate = new DateOnly(2026, 6, 20)
        });

        Assert.Equal(new InvoiceDetails(20_000, "Regular tenant", "Pay by transfer", new DateOnly(2026, 6, 20)), invoice.Details);
        Assert.Equal(3_030_000, invoice.TotalAmount);
    }

    [Fact]
    public async Task UpdateAsync_CustomItems_AreAddedEditedAndDeleted()
    {
        var invoice = SetupInvoiceForUpdate();
        var faucet = invoice.Items.Single(i => i.IsCustom);

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest>
            {
                new() { Id = faucet.Id, Quantity = 2 },
                new() { Description = "Cleaning", Quantity = 1, UnitPrice = 100_000 }
            }
        });

        Assert.Equal(100_000, faucet.Amount);
        Assert.Contains(invoice.Items, i => i is { Description: "Cleaning", Amount: 100_000 });
        Assert.Equal(3_200_000, invoice.Subtotal);

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = faucet.Id, IsDeleted = true } }
        });

        Assert.DoesNotContain(invoice.Items, i => i.Id == faucet.Id);
        Assert.Equal(3_100_000, invoice.Subtotal);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTheReloadedDetail()
    {
        var invoice = SetupInvoiceForUpdate();

        var response = await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { Notes = "New" });

        Assert.Equal(invoice.Id, response.Id);
        Assert.Equal("New", response.Notes);
        Assert.Equal("Sunrise House", response.PropertyName);
        Assert.Equal(2, response.Items.Count);
        _invoiceRepository.Verify(r => r.GetByIdWithDetailsAsync(invoice.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_DomainRuleBroken_PropagatesWithoutReloading()
    {
        var invoice = SetupInvoiceForUpdate();
        var rent = invoice.Items.Single(i => i.SourceType == InvoiceItemSourceType.Rent);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = rent.Id, IsDeleted = true } }
        }));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        _invoiceRepository.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_DiscountWithoutReasonAfterMerge_Throws()
    {
        var invoice = SetupInvoiceForUpdate();

        var ex = await Assert.ThrowsAsync<DomainException>(() => _invoiceService.UpdateAsync(
            invoice.Id, new UpdateInvoiceRequest { DiscountAmount = 30_000m, DiscountReason = null }));

        Assert.Equal("DiscountReason is required when DiscountAmount is greater than 0", ex.Message);
        Assert.Equal(10_000, invoice.DiscountAmount);
    }

    [Fact]
    public async Task UpdateAsync_DiscountWithTheCurrentReason_IsAllowed()
    {
        var invoice = SetupInvoiceForUpdate();

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { DiscountAmount = 30_000m });

        Assert.Equal(30_000, invoice.DiscountAmount);
        Assert.Equal("Old reason", invoice.DiscountReason);
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

    private static CancelInvoiceRequest CancelRequest => new() { Reason = "Wrong meter reading" };

    [Fact]
    public async Task CancelAsync_UnknownInvoice_ThrowsNotFound()
    {
        SetupInvoiceForUpdate(action: PermissionActions.Cancel);
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _invoiceService.CancelAsync(id, CancelRequest));

        Assert.Equal(NotFoundMessages.Invoice(id), ex.Message);
        _invoiceRepository.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_InvoiceOfAnotherOrganization_ThrowsNotFoundAndLeavesItUnchanged()
    {
        var invoice = SetupInvoiceForUpdate(Restricted(Guid.NewGuid()), PermissionActions.Cancel);

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _invoiceService.CancelAsync(invoice.Id, CancelRequest));

        Assert.Equal(NotFoundMessages.Invoice(invoice.Id), ex.Message);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Single(invoice.StatusHistories);
    }

    [Fact]
    public async Task CancelAsync_InvoiceOfAnAllowedOrganization_Cancels()
    {
        var invoice = SetupInvoiceForUpdate(action: PermissionActions.Cancel);
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Cancel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Restricted(invoice.OrganizationId));

        await _invoiceService.CancelAsync(invoice.Id, CancelRequest);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public async Task CancelAsync_ChecksInvoiceCancelScopeAndCancelsAsTheCurrentUser()
    {
        var invoice = SetupInvoiceForUpdate(action: PermissionActions.Cancel);

        var result = await _invoiceService.CancelAsync(invoice.Id, CancelRequest);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(InvoiceStatus.Cancelled, result.Status);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(_actor.Id, history.ChangedBy);
        Assert.Equal("Wrong meter reading", history.Reason);
        _organizationScopeAccessor.Verify(
            a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Cancel, It.IsAny<CancellationToken>()), Times.Once);
        _invoiceRepository.Verify(r => r.GetAggregateForUpdateAsync(invoice.Id, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_PaidInvoice_ThrowsConflictAndDoesNotReloadIt()
    {
        var invoice = SetupInvoiceForUpdate(action: PermissionActions.Cancel);
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Partial, PaidAmount = 1_000m });

        var ex = await Assert.ThrowsAsync<DomainException>(() => _invoiceService.CancelAsync(invoice.Id, CancelRequest));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        _invoiceRepository.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _paymentRepository.Verify(
            r => r.ListPendingForUpdateByInvoiceIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_VoidsThePendingPaymentsAsTheCurrentUser()
    {
        var invoice = SetupInvoiceForUpdate(action: PermissionActions.Cancel);
        var payment = invoice.RecordPayment(
            new PaymentDetails(1_000_000, PaymentMethod.Cash, DateTimeOffset.UtcNow.AddMinutes(-1), null, null, null, null),
            SentinelActors.System,
            DateTimeOffset.UtcNow);
        _paymentRepository
            .Setup(r => r.ListPendingForUpdateByInvoiceIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        await _invoiceService.CancelAsync(invoice.Id, CancelRequest);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(Payment.InvoiceCancelledReason, payment.VoidReason);
        Assert.Equal(_actor.Id, payment.VoidedBy);
        Assert.Equal(0, invoice.PaidAmount);
    }

    private (Guid InvoiceId, Guid OrganizationId) SetupStatusHistories(params InvoiceStatusHistory[] histories)
    {
        var invoiceId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        _invoiceRepository
            .Setup(r => r.GetOrganizationIdAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organizationId);
        _invoiceRepository
            .Setup(r => r.ListStatusHistoriesAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(histories.ToList());
        return (invoiceId, organizationId);
    }

    [Fact]
    public async Task GetStatusHistoriesAsync_UnknownInvoice_ThrowsNotFoundWithoutListing()
    {
        SetupScope(Unrestricted());
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _invoiceService.GetStatusHistoriesAsync(id));

        Assert.Equal(NotFoundMessages.Invoice(id), ex.Message);
        _invoiceRepository.Verify(
            r => r.ListStatusHistoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusHistoriesAsync_InvoiceOfAnotherOrganization_ThrowsNotFoundWithoutListing()
    {
        var (invoiceId, _) = SetupStatusHistories();
        SetupScope(Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _invoiceService.GetStatusHistoriesAsync(invoiceId));

        Assert.Equal(NotFoundMessages.Invoice(invoiceId), ex.Message);
        _invoiceRepository.Verify(
            r => r.ListStatusHistoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusHistoriesAsync_InvoiceOfAnAllowedOrganization_ChecksReadScopeAndMapsInRepositoryOrder()
    {
        var actorId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero);
        var invoiceId = Guid.NewGuid();
        var created = new InvoiceStatusHistory(invoiceId, null, InvoiceStatus.Pending, null, actorId, createdAt);
        var overdue = new InvoiceStatusHistory(
            invoiceId, InvoiceStatus.Pending, InvoiceStatus.Overdue, Invoice.OverdueReason, null, createdAt.AddDays(9));
        var (id, organizationId) = SetupStatusHistories(created, overdue);
        SetupScope(Restricted(organizationId));

        var result = await _invoiceService.GetStatusHistoriesAsync(id);

        Assert.Equal(2, result.Count);
        Assert.Equal(
            (created.Id, (InvoiceStatus?)null, InvoiceStatus.Pending, (string?)null, (Guid?)actorId, createdAt),
            (result[0].Id, result[0].OldStatus, result[0].NewStatus, result[0].Reason, result[0].ChangedBy, result[0].ChangedAt));
        Assert.Equal(
            (overdue.Id, (InvoiceStatus?)InvoiceStatus.Pending, InvoiceStatus.Overdue, (string?)Invoice.OverdueReason, (Guid?)null, createdAt.AddDays(9)),
            (result[1].Id, result[1].OldStatus, result[1].NewStatus, result[1].Reason, result[1].ChangedBy, result[1].ChangedAt));
        _organizationScopeAccessor.Verify(
            a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Read, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStatusHistoriesAsync_NoHistories_ReturnsEmpty()
    {
        var (invoiceId, _) = SetupStatusHistories();
        SetupScope(Unrestricted());

        Assert.Empty(await _invoiceService.GetStatusHistoriesAsync(invoiceId));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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

    // SetupInvoiceForUpdate: due 2026-06-08 (past), rent 3,000,000 + faucet 50,000 - discount 10,000 = 3,040,000 after Update
    [Fact]
    public async Task UpdateAsync_PastDueDate_MarksTheInvoiceOverdueWithThePropertyLateFee()
    {
        var invoice = SetupInvoiceForUpdate();
        SetupPropertyLateFee(invoice, LateFeeType.Fixed, 100_000);

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest());

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(100_000, invoice.LateFeeAmount);
        Assert.Equal(3_140_000, invoice.TotalAmount);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(Invoice.OverdueReason, history.Reason);
        Assert.Equal(_actor.Id, history.ChangedBy);
    }

    [Fact]
    public async Task UpdateAsync_PercentLateFee_IsChargedOnTheTotalAfterTheEdit()
    {
        var invoice = SetupInvoiceForUpdate();
        SetupPropertyLateFee(invoice, LateFeeType.Percent, 5);

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { DiscountAmount = 50_000m });

        // 5% of 3,000,000 (after the new discount), not of the total before the edit
        Assert.Equal(150_000, invoice.LateFeeAmount);
        Assert.Equal(3_150_000, invoice.TotalAmount);
    }

    [Fact]
    public async Task UpdateAsync_FutureDueDate_StaysPendingWithoutHistory()
    {
        var invoice = SetupInvoiceForUpdate();
        SetupPropertyLateFee(invoice, LateFeeType.Fixed, 100_000);

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(5) });

        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.Single(invoice.StatusHistories);
        _propertyRepository.Verify(r => r.GetByLeaseIdAsync(invoice.LeaseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_LaterDueDateOnAnOverdueInvoice_MovesItBackToPendingAndKeepsTheLateFee()
    {
        var invoice = SetupInvoiceForUpdate();
        SetupPropertyLateFee(invoice, LateFeeType.Fixed, 100_000);
        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest());

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(5) });

        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(100_000, invoice.LateFeeAmount);
        Assert.Equal(3_140_000, invoice.TotalAmount);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(InvoiceStatus.Overdue, history.OldStatus);
        Assert.Null(history.Reason);
        Assert.Equal(_actor.Id, history.ChangedBy);
    }

    [Fact]
    public async Task UpdateAsync_DiscountEqualToTheTotal_MarksTheInvoicePaid()
    {
        var invoice = SetupInvoiceForUpdate();
        SetupPropertyLateFee(invoice, LateFeeType.Fixed, 100_000);

        await _invoiceService.UpdateAsync(invoice.Id, new UpdateInvoiceRequest { DiscountAmount = 3_050_000m });

        Assert.Equal(0, invoice.TotalAmount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.False(invoice.IsEditable);
    }

    [Fact]
    public async Task UpdateAsync_DomainRuleBroken_DoesNotRefreshTheStatus()
    {
        var invoice = SetupInvoiceForUpdate();

        await Assert.ThrowsAsync<DomainException>(() => _invoiceService.UpdateAsync(
            invoice.Id, new UpdateInvoiceRequest { DueDate = new DateOnly(2026, 5, 31) }));

        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Single(invoice.StatusHistories);
        _propertyRepository.VerifyNoOtherCalls();
    }

    private void SetupCreate(Guid leaseId, Guid organizationId, OrganizationScope scope)
    {
        _leaseRepository
            .Setup(r => r.GetOrganizationIdAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organizationId);
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Create, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
    }

    private void SetupGenerateReturns(InvoiceGenerationResult result) =>
        _invoiceGenerator
            .Setup(g => g.GenerateAsync(It.IsAny<InvoiceDraft>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task CreateAsync_UnknownLease_ThrowsNotFoundWithoutGenerating()
    {
        var leaseId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _invoiceService.CreateAsync(new CreateInvoiceRequest { LeaseId = leaseId, BillingMonth = June.FirstDay }));

        Assert.Equal(NotFoundMessages.Lease(leaseId), ex.Message);
        _invoiceGenerator.Verify(
            g => g.GenerateAsync(It.IsAny<InvoiceDraft>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_LeaseOfAnotherOrganization_ThrowsNotFoundWithoutGenerating()
    {
        var leaseId = Guid.NewGuid();
        SetupCreate(leaseId, Guid.NewGuid(), Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _invoiceService.CreateAsync(new CreateInvoiceRequest { LeaseId = leaseId, BillingMonth = June.FirstDay }));

        Assert.Equal(NotFoundMessages.Lease(leaseId), ex.Message);
        _invoiceGenerator.Verify(
            g => g.GenerateAsync(It.IsAny<InvoiceDraft>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_GeneratesTheRequestAsTheCurrentUserAndReturnsTheReloadedInvoice()
    {
        var invoice = NewInvoice();
        var organizationId = Guid.NewGuid();
        SetupCreate(invoice.LeaseId, organizationId, Restricted(organizationId));
        SetupGenerateReturns(new InvoiceGenerationResult.Created(invoice.Id));
        _invoiceRepository
            .Setup(r => r.GetByIdWithDetailsAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var before = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await _invoiceService.CreateAsync(new CreateInvoiceRequest
        {
            LeaseId = invoice.LeaseId,
            BillingMonth = June.FirstDay,
            CustomItems = [new CreateInvoiceCustomItemRequest { Description = " Faucet repair ", Quantity = 2, UnitPrice = 50_000 }],
            DiscountAmount = 10_000,
            DiscountReason = "Loyal tenant",
            Notes = "Pay by transfer"
        });

        var after = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(invoice.Id, result.Id);
        _invoiceGenerator.Verify(g => g.GenerateAsync(
            It.Is<InvoiceDraft>(d =>
                d.LeaseId == invoice.LeaseId
                && d.BillingMonth == June
                && d.Today >= before && d.Today <= after
                && d.CustomLines.Single() == InvoiceLine.Custom("Faucet repair", 2, 50_000)
                && d.DiscountAmount == 10_000
                && d.DiscountReason == "Loyal tenant"
                && d.Notes == "Pay by transfer"),
            _actor.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_AlreadyInvoiced_ThrowsConflict()
    {
        var leaseId = Guid.NewGuid();
        SetupCreate(leaseId, Guid.NewGuid(), Unrestricted());
        SetupGenerateReturns(new InvoiceGenerationResult.Skipped(
        [
            new InvoiceIssue(InvoiceIssueCode.AlreadyInvoiced, "The lease already has an invoice for 2026-06"),
            new InvoiceIssue(InvoiceIssueCode.MissingReading, "No Electricity reading has been recorded for 2026-06")
        ]));

        var ex = await Assert.ThrowsAsync<AppConflictException>(
            () => _invoiceService.CreateAsync(new CreateInvoiceRequest { LeaseId = leaseId, BillingMonth = June.FirstDay }));

        Assert.Equal("The lease already has an invoice for 2026-06", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_CalculationErrors_ThrowsValidationKeyedByCamelCaseCode()
    {
        var leaseId = Guid.NewGuid();
        SetupCreate(leaseId, Guid.NewGuid(), Unrestricted());
        SetupGenerateReturns(new InvoiceGenerationResult.Skipped(
        [
            new InvoiceIssue(InvoiceIssueCode.MissingReading, "No Electricity reading has been recorded for 2026-06"),
            new InvoiceIssue(InvoiceIssueCode.MissingReading, "No Water reading has been recorded for 2026-06")
        ]));

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => _invoiceService.CreateAsync(new CreateInvoiceRequest { LeaseId = leaseId, BillingMonth = June.FirstDay }));

        var messages = Assert.Single(ex.Errors!, e => e.Key == "missingReading").Value;
        Assert.Equal(2, messages.Length);
    }

    [Fact]
    public async Task PreviewAsync_CalculatesTheRequestThroughTheGeneratorWithoutCreating()
    {
        // As GetByIdForBillingAsync loads it: Room and Room.Property included
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101");
        TestEntities.Set(room, new { Property = property });
        var lease = TestLeases.Active(new DateOnly(2026, 5, 1), room: room);
        _leaseRepository
            .Setup(r => r.GetByIdForBillingAsync(lease.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lease);
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Create, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Unrestricted());
        InvoiceLine[] lines = [new("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, lease.Id)];
        _invoiceGenerator
            .Setup(g => g.CalculateAsync(lease, It.IsAny<InvoiceDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceCalculation
            {
                BillingMonth = June,
                DueDate = new DateOnly(2026, 6, 8),
                VatRate = 0,
                DiscountAmount = 0,
                Lines = lines,
                Totals = InvoiceTotals.Compute(lines.Select(l => (l.SourceType, l.Amount)), 0, 0, 0),
                Errors = [],
                Warnings = []
            });

        await _invoiceService.PreviewAsync(new CreateInvoiceRequest
        {
            LeaseId = lease.Id,
            BillingMonth = June.FirstDay,
            DiscountAmount = 5_000,
            Notes = "Preview"
        });

        _invoiceGenerator.Verify(g => g.CalculateAsync(
            lease,
            It.Is<InvoiceDraft>(d => d.LeaseId == lease.Id && d.BillingMonth == June && d.DiscountAmount == 5_000 && d.CustomLines.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
        _invoiceGenerator.Verify(
            g => g.GenerateAsync(It.IsAny<InvoiceDraft>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
