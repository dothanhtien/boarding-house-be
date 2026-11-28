using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Jobs;

/// <summary>
/// Voids gateway payments (VNPay...) still <c>PENDING</c> <see cref="Payment.GatewayCheckoutLifetime"/> +
/// <see cref="Payment.GatewayVoidGrace"/> after they were created: the link expired and no IPN came, so the invoice is
/// free for a new attempt. Scheduled every 10 minutes by <c>RecurringJobRegistry</c> (Infrastructure); each payment is
/// re-checked under its lock in its own transaction, so it is safe to re-run. An IPN that still arrives later is
/// recorded for review by <c>OnlinePaymentService</c>.
/// </summary>
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

        var createdBefore = DateTimeOffset.UtcNow - Payment.GatewayCheckoutLifetime - Payment.GatewayVoidGrace;
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
