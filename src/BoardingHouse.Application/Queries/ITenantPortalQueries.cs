using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;

namespace BoardingHouse.Application.Queries;

public interface ITenantPortalQueries
{
    Task<TenancyResponse> GetTenancyAsync(Guid userId, DateOnly today, CancellationToken cancellationToken = default);

    Task<PagedResult<MyLeaseResponse>> SearchLeasesAsync(
        Guid userId, MyLeaseListQuery query, CancellationToken cancellationToken = default);
    Task<MyLeaseDetailResponse?> GetLeaseAsync(Guid userId, Guid leaseId, CancellationToken cancellationToken = default);
    Task<PagedResult<MyInvoiceResponse>> SearchInvoicesAsync(
        Guid userId, MyInvoiceListQuery query, CancellationToken cancellationToken = default);
    Task<MyInvoiceDetailResponse?> GetInvoiceAsync(Guid userId, Guid invoiceId, CancellationToken cancellationToken = default);
    Task<bool> IsInvoiceVisibleAsync(Guid userId, Guid invoiceId, CancellationToken cancellationToken = default);
    Task<bool> IsLeaseVisibleAsync(Guid userId, Guid leaseId, CancellationToken cancellationToken = default);
    Task<PagedResult<MyUtilityReadingResponse>> SearchUtilityReadingsAsync(
        Guid userId, Guid leaseId, MyUtilityReadingListQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<MyPaymentResponse>> SearchPaymentsAsync(
        Guid userId, MyPaymentListQuery query, CancellationToken cancellationToken = default);
}
