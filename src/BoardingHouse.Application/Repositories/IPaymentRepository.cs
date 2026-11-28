using System.Linq.Expressions;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Repositories;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetInvoiceIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Payment?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Payment>> ListPendingForUpdateByInvoiceIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<Payment?> FindPendingGatewayPaymentAsync(
        Guid invoiceId, DateTimeOffset createdAfter, CancellationToken cancellationToken = default);
    Task<List<Guid>> ListStaleGatewayPaymentIdsAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken = default);
    Task<List<Payment>> ListByInvoiceIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<PagedResult<Payment>> SearchAsync(
        Guid? organizationId,
        IReadOnlySet<Guid>? allowedOrganizationIds,
        Guid? propertyId,
        PaymentMethod? method,
        PaymentStatus? status,
        DateTimeOffset? paidFrom,
        DateTimeOffset? paidBefore,
        string? search,
        PageRequest pageRequest,
        Expression<Func<Payment, object>> sortField,
        SortOrder sortOrder,
        CancellationToken cancellationToken = default);
}
