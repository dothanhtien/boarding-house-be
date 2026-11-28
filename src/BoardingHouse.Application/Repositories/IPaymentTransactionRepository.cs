using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IPaymentTransactionRepository
{
    Task AddAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetLatestCheckoutAsync(Guid paymentId, CancellationToken cancellationToken = default);
}
