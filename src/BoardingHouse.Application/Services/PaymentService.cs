using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using Mapster;

namespace BoardingHouse.Application.Services;

public class PaymentService(
    IPaymentRepository paymentRepository,
    IInvoiceRepository invoiceRepository,
    ILeaseRepository leaseRepository,
    InvoiceLateFeeResolver lateFeeResolver,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<PaymentService> logger) : IPaymentService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<PaymentResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await paymentRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Payment(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            payment.Invoice!.OrganizationId,
            PermissionResources.Payment,
            PermissionActions.Read,
            NotFoundMessages.Payment(id),
            cancellationToken);

        return payment.Adapt<PaymentResponse>();
    }

    public async Task<PaymentResponse> RecordAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var paymentId = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var invoice = await invoiceRepository.GetAggregateForUpdateAsync(invoiceId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));

            await organizationScopeAccessor.EnsureScopeAsync(
                invoice.OrganizationId,
                PermissionResources.Payment,
                PermissionActions.Create,
                NotFoundMessages.Invoice(invoiceId),
                ct);

            if (request.Confirm)
            {
                var confirmScope = await organizationScopeAccessor.GetScopeAsync(
                    PermissionResources.Payment, PermissionActions.Confirm, ct);

                if (!confirmScope.Allows(invoice.OrganizationId))
                {
                    throw new AppForbiddenException("Confirm = true requires the payment:confirm permission");
                }
            }

            var details = new PaymentDetails(
                request.Amount,
                request.Method!.Value,
                request.PaymentDate!.Value,
                await ResolvePayerUserIdAsync(invoice.LeaseId, request, ct),
                request.PayerNameOverride,
                request.TransactionReference,
                request.Note);

            var actorId = currentUserAccessor.RequiredUser.Id;
            var now = DateTimeOffset.UtcNow;
            var payment = invoice.RecordPayment(details, actorId, now);
            await paymentRepository.AddAsync(payment, ct);

            if (request.Confirm)
            {
                invoice.ConfirmPayment(payment, actorId, now);
                invoice.RefreshStatus(Today, await lateFeeResolver.ResolveAsync(invoice, ct), actorId, now);
            }

            return payment.Id;
        }, cancellationToken);

        logger.LogInformation("Payment recorded ({PaymentId}) for invoice {InvoiceId}", paymentId, invoiceId);

        return await GetDetailAsync(paymentId, cancellationToken);
    }

    public async Task<PaymentResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (invoice, payment) = await LockAsync(id, PermissionActions.Confirm, ct);

            var actorId = currentUserAccessor.RequiredUser.Id;
            var now = DateTimeOffset.UtcNow;
            invoice.ConfirmPayment(payment, actorId, now);
            invoice.RefreshStatus(Today, await lateFeeResolver.ResolveAsync(invoice, ct), actorId, now);
        }, cancellationToken);

        logger.LogInformation("Payment confirmed ({PaymentId})", id);

        return await GetDetailAsync(id, cancellationToken);
    }

    public async Task<PaymentResponse> VoidAsync(Guid id, VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (invoice, payment) = await LockAsync(id, PermissionActions.Void, ct);

            var actorId = currentUserAccessor.RequiredUser.Id;
            var now = DateTimeOffset.UtcNow;
            invoice.VoidPayment(payment, request.Reason, actorId, now);
            invoice.RefreshStatus(Today, await lateFeeResolver.ResolveAsync(invoice, ct), actorId, now);
        }, cancellationToken);

        logger.LogInformation("Payment voided ({PaymentId})", id);

        return await GetDetailAsync(id, cancellationToken);
    }

    private async Task<(Invoice Invoice, Payment Payment)> LockAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        var invoiceId = await paymentRepository.GetInvoiceIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Payment(id));

        var invoice = await invoiceRepository.GetAggregateForUpdateAsync(invoiceId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Payment(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            invoice.OrganizationId,
            PermissionResources.Payment,
            action,
            NotFoundMessages.Payment(id),
            cancellationToken);

        var payment = await paymentRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Payment(id));

        return (invoice, payment);
    }

    private async Task<Guid?> ResolvePayerUserIdAsync(
        Guid leaseId, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.PayerNameOverride))
        {
            return null;
        }

        var tenants = await leaseRepository.ListTenantsAsync(leaseId, cancellationToken);

        if (request.PayerUserId is not { } payerUserId)
        {
            return tenants.FirstOrDefault(t => t.IsRepresentative)?.UserId;
        }

        return tenants.Any(t => t.UserId == payerUserId)
            ? payerUserId
            : throw new AppValidationException(
                "The payment cannot be recorded",
                new Dictionary<string, string[]> { ["payerUserId"] = ["PayerUserId must be a tenant of the lease"] });
    }

    private async Task<PaymentResponse> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Payment(id));

        return payment.Adapt<PaymentResponse>();
    }
}
