using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class InvoiceGenerationRunRepository(AppDbContext context) : IInvoiceGenerationRunRepository
{
    public Task AddAsync(InvoiceGenerationRun run, CancellationToken cancellationToken = default) =>
        context.InvoiceGenerationRuns.AddAsync(run, cancellationToken).AsTask();
}
