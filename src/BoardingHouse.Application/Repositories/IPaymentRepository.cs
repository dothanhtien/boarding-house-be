using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
}
