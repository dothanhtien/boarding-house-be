using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Jobs;

/// <summary>
/// Moves every <c>PENDING</c> / <c>PARTIAL</c> invoice whose <c>DueDate</c> is before today (UTC) to <c>OVERDUE</c>
/// and adds its late fee once (<c>Invoice.RefreshStatus</c>). Scheduled daily by <c>RecurringJobRegistry</c>
/// (Infrastructure); safe to re-run — each invoice is re-checked under its lock and handled in its own transaction,
/// so one failure doesn't stop the batch.
/// </summary>
public class MarkOverdueInvoicesJob(
    IInvoiceRepository invoiceRepository,
    InvoiceLateFeeResolver lateFeeResolver,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<MarkOverdueInvoicesJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        currentUserAccessor.IsSystemActor = true;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoiceIds = await invoiceRepository.ListIdsToMarkOverdueAsync(today, cancellationToken);

        var marked = 0;
        var failed = 0;

        foreach (var invoiceId in invoiceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await TryMarkOverdueAsync(invoiceId, today, cancellationToken))
                {
                    marked++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Mark invoice overdue failed ({InvoiceId})", invoiceId);
            }
            finally
            {
                unitOfWork.DiscardChanges();
            }
        }

        logger.LogInformation(
            "Mark overdue invoices finished: {Marked} marked, {Skipped} skipped, {Failed} failed",
            marked, invoiceIds.Count - marked - failed, failed);
    }

    private Task<bool> TryMarkOverdueAsync(Guid invoiceId, DateOnly today, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var invoice = await invoiceRepository.GetAggregateForUpdateAsync(invoiceId, ct);
            if (invoice is null) return false;

            var lateFee = await lateFeeResolver.ResolveAsync(invoice, ct);

            if (!invoice.RefreshStatus(today, lateFee, changedBy: null, DateTimeOffset.UtcNow)
                || invoice.Status != InvoiceStatus.Overdue)
            {
                return false;
            }

            logger.LogInformation(
                "Invoice marked overdue ({InvoiceId}, late fee {LateFeeAmount})", invoiceId, invoice.LateFeeAmount);

            return true;
        }, cancellationToken);
}
