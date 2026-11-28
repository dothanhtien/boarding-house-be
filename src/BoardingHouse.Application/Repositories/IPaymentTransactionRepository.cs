using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Repositories;

public interface IPaymentTransactionRepository
{
    Task AddAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetLatestCheckoutAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsSuccessfulCallbackAsync(
        PaymentGateway gateway, string gatewayTransactionId, CancellationToken cancellationToken = default);
}
