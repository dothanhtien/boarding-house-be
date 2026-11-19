using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
        await context.Payments.AddAsync(payment, cancellationToken);

    public Task<Payment?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDetails()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    private IQueryable<Payment> WithDetails() =>
        context.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Invoice!.DeletedAt == null)
            .Include(p => p.Invoice!).ThenInclude(i => i.Lease!).ThenInclude(l => l.Room!).ThenInclude(r => r.Property)
            .Include(p => p.PayerUser);
}
