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
}
