using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Converters;
using BoardingHouse.Infrastructure.Persistence.Listing;

namespace BoardingHouse.Infrastructure.Repositories;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    private static readonly string PendingStatus =
        (string)PaymentStatusDbValueConverter.Instance.ConvertToProvider(PaymentStatus.Pending)!;

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
        await context.Payments.AddAsync(payment, cancellationToken);

    public Task<Payment?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDetails()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Guid?> GetInvoiceIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Payments
            .Where(p => p.Id == id)
            .Select(p => p.InvoiceId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Payment?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Payments
            .FromSql($"SELECT * FROM payments WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<Payment>> ListPendingForUpdateByInvoiceIdAsync(
        Guid invoiceId, CancellationToken cancellationToken = default) =>
        context.Payments
            .FromSql($"SELECT * FROM payments WHERE invoice_id = {invoiceId} AND status = {PendingStatus} ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);

    public Task<Payment?> FindPendingGatewayPaymentAsync(
        Guid invoiceId, DateTimeOffset createdAfter, CancellationToken cancellationToken = default) =>
        context.Payments
            .Where(p => p.InvoiceId == invoiceId
                && p.Status == PaymentStatus.Pending
                && p.Method != PaymentMethod.Cash
                && p.Method != PaymentMethod.BankTransfer
                && p.CreatedAt > createdAfter)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<Payment>> ListByInvoiceIdAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
        WithDetails()
            .AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            .OrderBy(p => p.PaymentDate)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

    public Task<PagedResult<Payment>> SearchAsync(
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
        CancellationToken cancellationToken = default)
    {
        var payments = WithDetails().AsNoTracking();

        if (organizationId is not null)
        {
            payments = payments.Where(p => p.Invoice!.OrganizationId == organizationId);
        }
        else if (allowedOrganizationIds is not null)
        {
            payments = payments.Where(p => allowedOrganizationIds.Contains(p.Invoice!.OrganizationId));
        }

        if (propertyId is not null)
        {
            payments = payments.Where(p => p.Invoice!.Lease!.Room!.PropertyId == propertyId);
        }

        if (method is not null)
        {
            payments = payments.Where(p => p.Method == method);
        }

        if (status is not null)
        {
            payments = payments.Where(p => p.Status == status);
        }

        if (paidFrom is not null)
        {
            payments = payments.Where(p => p.PaymentDate >= paidFrom);
        }

        if (paidBefore is not null)
        {
            payments = payments.Where(p => p.PaymentDate < paidBefore);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search.Trim());
            payments = payments.Where(p =>
                EF.Functions.ILike(p.Invoice!.InvoiceNumber, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(p.Invoice!.Lease!.LeaseCode, pattern, LikePattern.EscapeCharacter)
                || (p.TransactionReference != null
                    && EF.Functions.ILike(p.TransactionReference, pattern, LikePattern.EscapeCharacter)));
        }

        return payments.ToPagedResultAsync(pageRequest, sortField, sortOrder, cancellationToken);
    }

    private IQueryable<Payment> WithDetails() =>
        context.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Invoice!.DeletedAt == null)
            .Include(p => p.Invoice!).ThenInclude(i => i.Lease!).ThenInclude(l => l.Room!).ThenInclude(r => r.Property)
            .Include(p => p.PayerUser);
}
