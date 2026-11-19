using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
        await context.Payments.AddAsync(payment, cancellationToken);
}
