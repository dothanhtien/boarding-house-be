using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Jobs;

// Frees the invoice of a gateway payment whose link expired without an IPN. An IPN that still arrives later is
// flagged for review by OnlinePaymentService
public class VoidExpiredOnlinePaymentsJob(
    IPaymentRepository paymentRepository,
    IInvoiceRepository invoiceRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<VoidExpiredOnlinePaymentsJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        currentUserAccessor.IsSystemActor = true;

        var createdBefore = DateTimeOffset.UtcNow - Payment.GatewayPendingWindow;
        var paymentIds = await paymentRepository.ListStaleGatewayPaymentIdsAsync(createdBefore, cancellationToken);

        var voided = 0;
        var failed = 0;

        foreach (var paymentId in paymentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await TryVoidAsync(paymentId, cancellationToken))
                {
                    voided++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Void expired online payment failed ({PaymentId})", paymentId);
            }
            finally
            {
                unitOfWork.DiscardChanges();
            }
        }

        logger.LogInformation(
            "Void expired online payments finished: {Voided} voided, {Skipped} skipped, {Failed} failed",
            voided, paymentIds.Count - voided - failed, failed);
    }

    private Task<bool> TryVoidAsync(Guid paymentId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var invoiceId = await paymentRepository.GetInvoiceIdAsync(paymentId, ct);
            var invoice = invoiceId is { } id ? await invoiceRepository.GetAggregateForUpdateAsync(id, ct) : null;
            var payment = invoice is null ? null : await paymentRepository.GetForUpdateAsync(paymentId, ct);

            if (invoice is null || payment is null || payment.Status != PaymentStatus.Pending)
            {
                return false;
            }

            invoice.VoidPayment(payment, Payment.GatewayExpiredReason, voidedBy: null, DateTimeOffset.UtcNow);
            logger.LogInformation("Expired online payment voided ({PaymentId})", paymentId);

            return true;
        }, cancellationToken);
}
