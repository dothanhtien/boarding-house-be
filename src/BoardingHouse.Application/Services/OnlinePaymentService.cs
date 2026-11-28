using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Application.Services.Security;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public class OnlinePaymentService(
    IEnumerable<IPaymentGateway> gateways,
    ITenantPortalQueries tenantPortalQueries,
    IInvoiceRepository invoiceRepository,
    IPaymentRepository paymentRepository,
    IPaymentTransactionRepository paymentTransactionRepository,
    IOrganizationPaymentGatewayRepository organizationPaymentGatewayRepository,
    ISecretProtector secretProtector,
    InvoiceLateFeeResolver lateFeeResolver,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<OnlinePaymentService> logger) : IOnlinePaymentService
{
    public const string PaymentInProgressMessage =
        "Another online payment of this invoice is in progress, try again in a few minutes";
    public const string GatewayUnavailableMessage = "The payment gateway could not start the payment, try again later";

    private const string UnknownPaymentError = "Unknown payment reference";
    private const string InvalidSignatureError = "Invalid signature";
    private const string AmountMismatchError = "Amount does not match the payment";
    private const string AlreadyConfirmedError = "The payment was already confirmed by another gateway transaction";
    private const string PaidAfterVoidError = "Paid after the payment was voided";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<List<PaymentGateway>> GetMyGatewaysAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        await EnsureMyInvoiceAsync(invoiceId, cancellationToken);

        var organizationId = await invoiceRepository.GetOrganizationIdAsync(invoiceId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));
        var accounts = await organizationPaymentGatewayRepository.ListByOrganizationIdAsync(organizationId, cancellationToken);

        return accounts
            .Where(a => a.IsActive && gateways.Any(g => g.Gateway == a.Gateway))
            .Select(a => a.Gateway)
            .ToList();
    }

    public async Task<OnlineCheckoutResponse> CreateMyCheckoutAsync(
        Guid invoiceId, CreateOnlineCheckoutRequest request, string clientIp, CancellationToken cancellationToken = default)
    {
        var gateway = request.Gateway!.Value;
        var adapter = GetAdapter(gateway);
        var userId = currentUserAccessor.RequiredUser.Id;

        await EnsureMyInvoiceAsync(invoiceId, cancellationToken);

        var start = await unitOfWork.ExecuteInTransactionAsync<CheckoutStart>(async ct =>
        {
            var invoice = await invoiceRepository.GetAggregateForUpdateAsync(invoiceId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));
            var now = DateTimeOffset.UtcNow;

            var pending = await paymentRepository.FindPendingGatewayPaymentAsync(
                invoiceId, now - Payment.GatewayCheckoutLifetime, ct);
            if (pending is not null)
            {
                var checkout = await paymentTransactionRepository.GetLatestCheckoutAsync(pending.Id, ct);

                return pending.Method == gateway.ToPaymentMethod()
                    && checkout is { Status: PaymentTransactionStatus.Pending, PaymentUrl: not null }
                    && checkout.ExpiresAt > now
                        ? new ReusedCheckout(ToResponse(pending.Id, gateway, checkout))
                        : throw new AppConflictException(PaymentInProgressMessage);
            }

            var account = await organizationPaymentGatewayRepository.GetAsync(invoice.OrganizationId, gateway, ct)
                ?? throw new AppConflictException(OrganizationPaymentGateway.NotAvailableMessage);
            account.EnsureActive();

            var lateFee = await lateFeeResolver.ResolveAsync(invoice, ct);
            invoice.RefreshStatus(Today, lateFee, changedBy: null, now);

            var amount = invoice.GetAmountToRequest();
            var payment = invoice.RecordPayment(
                new PaymentDetails(
                    amount,
                    gateway.ToPaymentMethod(),
                    PaymentDate: now,
                    PayerUserId: userId,
                    PayerNameOverride: null,
                    TransactionReference: null,
                    Note: null),
                userId,
                now);
            await paymentRepository.AddAsync(payment, ct);

            return new NewCheckout(
                invoice.Id,
                payment.Id,
                amount,
                invoice.InvoiceNumber,
                now,
                new GatewayCredentials(account.MerchantCode, secretProtector.Unprotect(account.SecretEncrypted)));
        }, cancellationToken);

        if (start is ReusedCheckout reused)
        {
            return reused.Response;
        }

        var created = (NewCheckout)start;
        var expiresAt = created.CreatedAt + Payment.GatewayCheckoutLifetime;

        GatewayCheckout gatewayCheckout;
        try
        {
            gatewayCheckout = await adapter.CreateCheckoutAsync(
                new GatewayCheckoutRequest(
                    created.PaymentId,
                    created.InvoiceId,
                    created.Amount,
                    $"Invoice {created.InvoiceNumber}",
                    clientIp,
                    created.CreatedAt,
                    expiresAt,
                    created.Credentials),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            await RecordCheckoutFailureAsync(created, gateway, ex, cancellationToken);
            throw new AppBadGatewayException(GatewayUnavailableMessage);
        }

        var transaction = PaymentTransaction.Checkout(
            created.PaymentId,
            gateway,
            created.Amount,
            gatewayCheckout.PaymentUrl,
            expiresAt,
            gatewayCheckout.RequestPayload,
            gatewayCheckout.ResponsePayload,
            DateTimeOffset.UtcNow);
        await paymentTransactionRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Online payment started ({PaymentId}, {Gateway}) for invoice {InvoiceId}", created.PaymentId, gateway, invoiceId);

        return ToResponse(created.PaymentId, gateway, transaction);
    }

    public async Task<GatewayHttpResponse> HandleCallbackAsync(
        PaymentGateway gateway, GatewayCallback callback, CancellationToken cancellationToken = default)
    {
        // Anonymous request from the gateway: every write is the system's
        currentUserAccessor.IsSystemActor = true;

        var adapter = gateways.SingleOrDefault(g => g.Gateway == gateway)
            ?? throw new AppNotFoundException($"Payment gateway '{gateway}' is not supported");

        GatewayCallbackData? data = null;
        try
        {
            data = adapter.ParseCallback(callback);
            var parsed = data;

            var outcome = await unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessCallbackAsync(adapter, callback, parsed, ct), cancellationToken);

            return adapter.BuildCallbackResponse(outcome);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == UniqueIndexNames.PaymentTransactionGatewayGatewayTransactionId)
        {
            // The same IPN delivered twice at once: the other delivery committed first
            unitOfWork.DiscardChanges();
            return adapter.BuildCallbackResponse(GatewayCallbackOutcome.AlreadyProcessed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unitOfWork.DiscardChanges();
            logger.LogError(ex, "Payment gateway callback failed ({Gateway}, payment {PaymentId})", gateway, data?.PaymentId);
            return adapter.BuildCallbackResponse(GatewayCallbackOutcome.Error);
        }
    }

    private async Task<GatewayCallbackOutcome> ProcessCallbackAsync(
        IPaymentGateway adapter, GatewayCallback callback, GatewayCallbackData data, CancellationToken ct)
    {
        var gateway = adapter.Gateway;
        var now = DateTimeOffset.UtcNow;

        // 1. The payment the callback names (not trusted yet), locked with its invoice
        var invoiceId = data.PaymentId is { } paymentId ? await paymentRepository.GetInvoiceIdAsync(paymentId, ct) : null;
        var invoice = invoiceId is { } id ? await invoiceRepository.GetAggregateForUpdateAsync(id, ct) : null;
        var payment = invoice is null ? null : await paymentRepository.GetForUpdateAsync(data.PaymentId!.Value, ct);

        if (invoice is null || payment is null || payment.Method != gateway.ToPaymentMethod())
        {
            await RecordCallbackAsync(null, gateway, PaymentTransactionStatus.Failed, data, UnknownPaymentError, false, now, ct);
            logger.LogWarning("Payment gateway callback for an unknown payment ({Gateway}, {PaymentId})", gateway, data.PaymentId);
            return GatewayCallbackOutcome.PaymentNotFound;
        }

        // 2. Signed with the secret of the organization the payment belongs to (even if the gateway was turned off
        // since: a late IPN for a payment already started must still count)
        var account = await organizationPaymentGatewayRepository.GetAsync(invoice.OrganizationId, gateway, ct);
        if (account is null
            || !adapter.VerifyCallback(
                callback, new GatewayCredentials(account.MerchantCode, secretProtector.Unprotect(account.SecretEncrypted))))
        {
            await RecordCallbackAsync(payment.Id, gateway, PaymentTransactionStatus.Failed, data, InvalidSignatureError, false, now, ct);
            logger.LogWarning("Payment gateway callback with an invalid signature ({Gateway}, {PaymentId})", gateway, payment.Id);
            return GatewayCallbackOutcome.InvalidSignature;
        }

        // 3. Delivered again after it was handled
        if (data.GatewayTransactionId is { } transactionId
            && await paymentTransactionRepository.ExistsSuccessfulCallbackAsync(gateway, transactionId, ct))
        {
            return GatewayCallbackOutcome.AlreadyProcessed;
        }

        // 4. Exactly what we asked for
        if (data.Amount != payment.Amount)
        {
            await RecordCallbackAsync(
                payment.Id, gateway, PaymentTransactionStatus.Failed, data, AmountMismatchError, data.IsSuccess, now, ct);
            logger.Log(
                data.IsSuccess ? LogLevel.Error : LogLevel.Warning,
                "Payment gateway reported {ReportedAmount} for payment {PaymentId} of {Amount} ({Gateway})",
                data.Amount, payment.Id, payment.Amount, gateway);
            return GatewayCallbackOutcome.InvalidAmount;
        }

        // 5. Cancelled, expired, declined: free the invoice for a new attempt
        if (!data.IsSuccess)
        {
            if (payment.Status == PaymentStatus.Pending)
            {
                invoice.VoidPayment(payment, Payment.GatewayFailedReason, voidedBy: null, now);
            }

            await RecordCallbackAsync(payment.Id, gateway, PaymentTransactionStatus.Failed, data, null, false, now, ct);
            return GatewayCallbackOutcome.Received;
        }

        // 6. Money received
        if (payment.Status == PaymentStatus.Confirmed && payment.TransactionReference == data.GatewayTransactionId)
        {
            return GatewayCallbackOutcome.AlreadyProcessed;
        }

        var notApplied = payment.Status switch
        {
            PaymentStatus.Confirmed => AlreadyConfirmedError,
            PaymentStatus.Voided => PaidAfterVoidError,
            _ => null
        };

        if (notApplied is null)
        {
            try
            {
                var lateFee = await lateFeeResolver.ResolveAsync(invoice, ct);
                invoice.RefreshStatus(Today, lateFee, changedBy: null, now);
                invoice.ConfirmPayment(payment, confirmedBy: null, now, data.GatewayTransactionId);
                invoice.RefreshStatus(Today, lateFee, changedBy: null, now);
            }
            catch (DomainException ex)
            {
                // Cancelled invoice, or already paid (cash, another online payment): the money is at the merchant but
                // cannot count on this invoice
                invoice.VoidPayment(payment, Payment.GatewayNotAppliedReason, voidedBy: null, now);
                notApplied = ex.Message;
            }
        }

        await RecordCallbackAsync(
            payment.Id, gateway, PaymentTransactionStatus.Success, data, notApplied, notApplied is not null, now, ct);

        if (notApplied is null)
        {
            logger.LogInformation(
                "Online payment confirmed ({PaymentId}, {Gateway}, transaction {GatewayTransactionId})",
                payment.Id, gateway, data.GatewayTransactionId);
        }
        else
        {
            logger.LogError(
                "Online payment received but not applied, refund or record it by hand ({PaymentId}, {Gateway}, transaction {GatewayTransactionId}): {Reason}",
                payment.Id, gateway, data.GatewayTransactionId, notApplied);
        }

        return GatewayCallbackOutcome.Received;
    }

    private Task RecordCallbackAsync(
        Guid? paymentId,
        PaymentGateway gateway,
        PaymentTransactionStatus status,
        GatewayCallbackData data,
        string? error,
        bool needsReview,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        paymentTransactionRepository.AddAsync(
            PaymentTransaction.Callback(
                paymentId, gateway, status, data.GatewayTransactionId, data.Amount, data.Payload, error, needsReview, now),
            cancellationToken);

    private async Task RecordCheckoutFailureAsync(
        NewCheckout created, PaymentGateway gateway, PaymentGatewayException ex, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            ex, "Payment gateway {Gateway} did not start payment {PaymentId} (timeout: {IsTimeout})",
            gateway, created.PaymentId, ex.IsTimeout);

        unitOfWork.DiscardChanges();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            await paymentTransactionRepository.AddAsync(
                PaymentTransaction.CheckoutFailed(
                    created.PaymentId, gateway, created.Amount, ex.RequestPayload, ex.ResponsePayload, ex.Message, now),
                ct);

            if (ex.IsTimeout)
            {
                return;
            }

            var invoice = await invoiceRepository.GetAggregateForUpdateAsync(created.InvoiceId, ct)
                ?? throw new InvalidOperationException($"Invoice '{created.InvoiceId}' of payment '{created.PaymentId}' not found");
            var payment = await paymentRepository.GetForUpdateAsync(created.PaymentId, ct)
                ?? throw new InvalidOperationException($"Payment '{created.PaymentId}' not found");

            if (payment.Status == PaymentStatus.Pending)
            {
                invoice.VoidPayment(payment, Payment.GatewayRequestFailedReason, voidedBy: null, now);
            }
        }, cancellationToken);
    }

    private async Task EnsureMyInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!await tenantPortalQueries.IsInvoiceVisibleAsync(currentUserAccessor.RequiredUser.Id, invoiceId, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));
        }
    }

    private IPaymentGateway GetAdapter(PaymentGateway gateway) =>
        gateways.SingleOrDefault(g => g.Gateway == gateway)
            ?? throw new AppConflictException(OrganizationPaymentGateway.NotAvailableMessage);

    private static OnlineCheckoutResponse ToResponse(Guid paymentId, PaymentGateway gateway, PaymentTransaction checkout) => new()
    {
        PaymentId = paymentId,
        Gateway = gateway,
        Amount = checkout.Amount!.Value,
        PaymentUrl = checkout.PaymentUrl!,
        ExpiresAt = checkout.ExpiresAt!.Value
    };

    private abstract record CheckoutStart;

    private sealed record ReusedCheckout(OnlineCheckoutResponse Response) : CheckoutStart;

    private sealed record NewCheckout(
        Guid InvoiceId,
        Guid PaymentId,
        decimal Amount,
        string InvoiceNumber,
        DateTimeOffset CreatedAt,
        GatewayCredentials Credentials) : CheckoutStart;
}
