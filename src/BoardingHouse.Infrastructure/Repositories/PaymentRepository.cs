using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Converters;

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

    private IQueryable<Payment> WithDetails() =>
        context.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Invoice!.DeletedAt == null)
            .Include(p => p.Invoice!).ThenInclude(i => i.Lease!).ThenInclude(l => l.Room!).ThenInclude(r => r.Property)
            .Include(p => p.PayerUser);
}
