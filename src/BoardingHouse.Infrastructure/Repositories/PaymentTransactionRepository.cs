using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Infrastructure.Repositories;

public class PaymentTransactionRepository(AppDbContext context) : IPaymentTransactionRepository
{
    public async Task AddAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions.AddAsync(transaction, cancellationToken);

    public Task<PaymentTransaction?> GetLatestCheckoutAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.PaymentId == paymentId && t.Type == PaymentTransactionType.Checkout)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsSuccessfulCallbackAsync(
        PaymentGateway gateway, string gatewayTransactionId, CancellationToken cancellationToken = default) =>
        context.PaymentTransactions.AnyAsync(
            t => t.Gateway == gateway
                && t.GatewayTransactionId == gatewayTransactionId
                && t.Status == PaymentTransactionStatus.Success,
            cancellationToken);
}
