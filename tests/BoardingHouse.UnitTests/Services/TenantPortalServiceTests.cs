using Moq;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Entities;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class TenantPortalServiceTests
{
    private readonly Mock<ITenantPortalQueries> _queries = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly TenantPortalService _service;

    public TenantPortalServiceTests()
    {
        var currentUserAccessor = new Mock<ICurrentUserAccessor>();
        currentUserAccessor.SetupGet(a => a.RequiredUser).Returns(TestEntities.New<User>(new { Id = _userId, FullName = "Tenant" }));
        _service = new TenantPortalService(_queries.Object, currentUserAccessor.Object);
    }

    [Fact]
    public async Task GetLeasesAsync_ScopesToCurrentUser()
    {
        var query = new MyLeaseListQuery();

        await _service.GetLeasesAsync(query);

        _queries.Verify(q => q.SearchLeasesAsync(_userId, query, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetLeaseAsync_NotVisible_ThrowsNotFound()
    {
        var leaseId = Guid.NewGuid();

        await Assert.ThrowsAsync<AppNotFoundException>(() => _service.GetLeaseAsync(leaseId));
        _queries.Verify(q => q.GetLeaseAsync(_userId, leaseId, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetInvoicesAsync_ScopesToCurrentUser()
    {
        var query = new MyInvoiceListQuery();

        await _service.GetInvoicesAsync(query);

        _queries.Verify(q => q.SearchInvoicesAsync(_userId, query, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetInvoiceAsync_NotVisible_ThrowsNotFound()
    {
        var invoiceId = Guid.NewGuid();

        await Assert.ThrowsAsync<AppNotFoundException>(() => _service.GetInvoiceAsync(invoiceId));
        _queries.Verify(q => q.GetInvoiceAsync(_userId, invoiceId, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetUtilityReadingsAsync_LeaseNotVisible_ThrowsNotFound_WithoutSearching()
    {
        var leaseId = Guid.NewGuid();

        await Assert.ThrowsAsync<AppNotFoundException>(
            () => _service.GetUtilityReadingsAsync(leaseId, new MyUtilityReadingListQuery()));
        _queries.Verify(q => q.IsLeaseVisibleAsync(_userId, leaseId, It.IsAny<CancellationToken>()));
        _queries.Verify(q => q.SearchUtilityReadingsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MyUtilityReadingListQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUtilityReadingsAsync_LeaseVisible_SearchesForCurrentUser()
    {
        var leaseId = Guid.NewGuid();
        var query = new MyUtilityReadingListQuery();
        _queries.Setup(q => q.IsLeaseVisibleAsync(_userId, leaseId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.GetUtilityReadingsAsync(leaseId, query);

        _queries.Verify(q => q.SearchUtilityReadingsAsync(_userId, leaseId, query, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetPaymentsAsync_ScopesToCurrentUser()
    {
        var query = new MyPaymentListQuery();

        await _service.GetPaymentsAsync(query);

        _queries.Verify(q => q.SearchPaymentsAsync(_userId, query, It.IsAny<CancellationToken>()));
    }
}
