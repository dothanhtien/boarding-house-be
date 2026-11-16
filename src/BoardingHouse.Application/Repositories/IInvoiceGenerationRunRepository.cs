using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IInvoiceGenerationRunRepository
{
    Task AddAsync(InvoiceGenerationRun run, CancellationToken cancellationToken = default);
}
