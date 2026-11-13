using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IInvoiceRepository : IRepository<Invoice>
{
    Task<bool> ExistsLiveForLeaseMonthAsync(Guid leaseId, BillingMonth billingMonth, CancellationToken cancellationToken = default);
    Task<bool> ExistsLiveDepositItemAsync(Guid leaseId, CancellationToken cancellationToken = default);
    Task<Invoice?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> NextNumberAsync(Guid organizationId, BillingMonth billingMonth, CancellationToken cancellationToken = default);
}
