using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Repositories;

public interface IInvoiceRepository : IRepository<Invoice>
{
    Task<bool> ExistsLiveForLeaseMonthAsync(Guid leaseId, BillingMonth billingMonth, CancellationToken cancellationToken = default);
    Task<bool> ExistsLiveDepositItemAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<Invoice?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> NextNumberAsync(Guid organizationId, BillingMonth billingMonth, CancellationToken cancellationToken = default);
    Task<PagedResult<Invoice>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        Guid? roomId,
        Guid? leaseId,
        InvoiceStatus? status,
        BillingMonth? billingMonth,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Invoice, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
}
