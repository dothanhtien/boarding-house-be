using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.DTOs.TenantPortal;
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
    public const string PaymentAwaitingConfirmationMessage =
        "A payment of this invoice is waiting for confirmation by the staff";

    public static readonly TimeSpan MinReusedCheckoutLifetime = TimeSpan.FromMinutes(2);

    private const string UnknownPaymentError = "Unknown payment reference";
    private const string InvalidSignatureError = "Invalid signature";
    private const string AmountMismatchError = "Amount does not match the payment";
    private const string AlreadyConfirmedError = "The payment was already confirmed by another gateway transaction";
    private const string PaidAfterVoidError = "Paid after the payment was voided";
    private const string SuspiciousDebitError = "Debited but held as suspicious by the gateway";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<List<MyOnlinePaymentGatewayResponse>> GetMyGatewaysAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        await EnsureMyInvoiceAsync(invoiceId, cancellationToken);

        var organizationId = await invoiceRepository.GetOrganizationIdAsync(invoiceId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));
        var accounts = await organizationPaymentGatewayRepository.ListByOrganizationIdAsync(organizationId, cancellationToken);

        return accounts
            .Where(a => a.IsActive && gateways.Any(g => g.Gateway == a.Gateway))
            .Select(a => new MyOnlinePaymentGatewayResponse { Gateway = a.Gateway })
            .ToList();
    }

    public async Task<MyOnlineCheckoutResponse> CreateMyCheckoutAsync(
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

            if (await paymentRepository.ExistsPendingManualPaymentAsync(invoiceId, ct))
            {
                throw new AppConflictException(PaymentAwaitingConfirmationMessage);
            }

            var pending = await paymentRepository.FindPendingGatewayPaymentAsync(
                invoiceId, now - Payment.GatewayPendingWindow, ct);
            var lateFee = await lateFeeResolver.ResolveAsync(invoice, ct);
            invoice.RefreshStatus(Today, lateFee, changedBy: null, now);

            if (pending is not null)
            {
                var checkout = await paymentTransactionRepository.GetLatestCheckoutAsync(pending.Id, ct);

                return pending.Method == gateway.ToPaymentMethod()
                    && checkout is { Status: PaymentTransactionStatus.Pending, PaymentUrl: not null }
                    && checkout.ExpiresAt > now + MinReusedCheckoutLifetime
                    && invoice.GetAmountToRequest() == pending.Amount
                        ? new ReusedCheckout(ToResponse(pending.Id, gateway, checkout))
                        : throw new AppConflictException(PaymentInProgressMessage);
            }

            var account = await organizationPaymentGatewayRepository.GetAsync(invoice.OrganizationId, gateway, ct)
                ?? throw new AppConflictException(OrganizationPaymentGateway.NotAvailableMessage);
            account.EnsureActive();

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
                new GatewayCredentials(account.MerchantCode, secretProtector.Unprotect(account.SecretEncrypted)),
                account.CredentialsVersion);
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
            created.CredentialsVersion,
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

        var adapter = GetCallbackAdapter(gateway);

        GatewayCallbackData data;
        try
        {
            data = adapter.ParseCallback(callback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Payment gateway callback could not be read ({Gateway})", gateway);
            return adapter.BuildCallbackResponse(GatewayCallbackOutcome.Error);
        }

        var outcome = await RunCallbackAsync(adapter, callback, data, PaymentTransactionType.Callback, cancellationToken);
        return adapter.BuildCallbackResponse(outcome);
    }

    public async Task<MyOnlinePaymentReturnResponse> HandleMyReturnAsync(
        PaymentGateway gateway, OnlinePaymentReturnRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUserAccessor.RequiredUser.Id;
        var adapter = GetCallbackAdapter(gateway);
        var callback = new GatewayCallback(request.Params!, Body: null);
        var data = adapter.ParseCallback(callback);
        var paymentId = data.PaymentId ?? throw new AppNotFoundException("Payment reference not found");
        var invoiceId = await paymentRepository.GetInvoiceIdAsync(paymentId, cancellationToken);
        if (invoiceId is null
            || !await tenantPortalQueries.IsInvoiceVisibleAsync(userId, invoiceId.Value, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Payment(paymentId));
        }

        var outcome = await RunCallbackAsync(adapter, callback, data, PaymentTransactionType.Return, cancellationToken);

        var payment = await paymentRepository.GetByIdWithDetailsAsync(paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment '{paymentId}' disappeared while its return was handled");

        logger.LogInformation(
            "Online payment return handled ({PaymentId}, {Gateway}, sent by user {UserId}): {Outcome}, payment {PaymentStatus}",
            paymentId, gateway, userId, outcome, payment.Status);

        return new MyOnlinePaymentReturnResponse
        {
            PaymentId = paymentId,
            InvoiceId = invoiceId.Value,
            Outcome = outcome,
            PaymentStatus = payment.Status
        };
    }

    private async Task<GatewayCallbackOutcome> RunCallbackAsync(
        IPaymentGateway adapter,
        GatewayCallback callback,
        GatewayCallbackData data,
        PaymentTransactionType source,
        CancellationToken cancellationToken)
    {
        currentUserAccessor.IsSystemActor = true;

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessCallbackAsync(adapter, callback, data, source, ct), cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == UniqueIndexNames.PaymentTransactionGatewayGatewayTransactionId)
        {
            // The same transaction delivered twice at once (two IPNs, or the IPN and the return): the other committed first
            unitOfWork.DiscardChanges();
            return GatewayCallbackOutcome.AlreadyProcessed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unitOfWork.DiscardChanges();
            logger.LogError(
                ex, "Payment gateway {Source} failed ({Gateway}, payment {PaymentId})", source, adapter.Gateway, data.PaymentId);
            return GatewayCallbackOutcome.Error;
        }
    }

    private IPaymentGateway GetCallbackAdapter(PaymentGateway gateway) =>
        gateways.SingleOrDefault(g => g.Gateway == gateway)
            ?? throw new AppNotFoundException($"Payment gateway '{gateway}' is not supported");

    private async Task<GatewayCallbackOutcome> ProcessCallbackAsync(
        IPaymentGateway adapter,
        GatewayCallback callback,
        GatewayCallbackData data,
        PaymentTransactionType source,
        CancellationToken ct)
    {
        var gateway = adapter.Gateway;
        var now = DateTimeOffset.UtcNow;

        var invoiceId = data.PaymentId is { } paymentId ? await paymentRepository.GetInvoiceIdAsync(paymentId, ct) : null;
        if (invoiceId is null)
        {
            // No account to check the signature with: only logged, so an anonymous caller cannot fill payment_transactions
            logger.LogWarning("Payment gateway callback for an unknown payment ({Gateway}, {PaymentId})", gateway, data.PaymentId);
            return GatewayCallbackOutcome.PaymentNotFound;
        }

        var invoice = await invoiceRepository.GetAggregateForUpdateAsync(invoiceId.Value, ct);
        var payment = invoice is null ? null : await paymentRepository.GetForUpdateAsync(data.PaymentId!.Value, ct);

        if (invoice is null || payment is null || payment.Method != gateway.ToPaymentMethod())
        {
            // Money may have been received for a payment that cannot take it (invoice deleted, another method),
            // but only a signed callback says so
            var organizationId = invoice?.OrganizationId
                ?? await invoiceRepository.GetOrganizationIdIncludingDeletedAsync(invoiceId.Value, ct);
            var paymentAccount = organizationId is { } orgId
                ? await organizationPaymentGatewayRepository.GetAsync(orgId, gateway, ct)
                : null;
            var isSigned = paymentAccount is not null
                && await IsSignedByAccountAsync(adapter, callback, paymentAccount, data.PaymentId!.Value, ct);
            var needsReview = isSigned && data.MayHaveDebited;

            await RecordCallbackAsync(
                data.PaymentId, gateway, PaymentTransactionStatus.Failed, data,
                isSigned ? UnknownPaymentError : InvalidSignatureError, needsReview, now, source, ct);
            logger.Log(
                needsReview ? LogLevel.Error : LogLevel.Warning,
                "Payment gateway callback for a payment that cannot take it ({Gateway}, {PaymentId}, signed: {IsSigned})",
                gateway, data.PaymentId, isSigned);
            return isSigned ? GatewayCallbackOutcome.PaymentNotFound : GatewayCallbackOutcome.InvalidSignature;
        }

        // Accepted even if the gateway was turned off since (a late IPN must still count), and with the credentials
        // the payment link was signed with if they were replaced since
        var account = await organizationPaymentGatewayRepository.GetAsync(invoice.OrganizationId, gateway, ct);
        if (account is null || !await IsSignedByAccountAsync(adapter, callback, account, payment.Id, ct))
        {
            await RecordCallbackAsync(payment.Id, gateway, PaymentTransactionStatus.Failed, data, InvalidSignatureError, false, now, source, ct);
            logger.LogWarning("Payment gateway callback with an invalid signature ({Gateway}, {PaymentId})", gateway, payment.Id);
            return GatewayCallbackOutcome.InvalidSignature;
        }

        if (data.GatewayTransactionId is { } transactionId
            && await paymentTransactionRepository.ExistsSuccessfulCallbackAsync(gateway, transactionId, ct))
        {
            return GatewayCallbackOutcome.AlreadyProcessed;
        }

        if (data.Amount != payment.Amount)
        {
            await RecordCallbackAsync(
                payment.Id, gateway, PaymentTransactionStatus.Failed, data, AmountMismatchError, data.MayHaveDebited, now,
                source, ct);
            logger.Log(
                data.MayHaveDebited ? LogLevel.Error : LogLevel.Warning,
                "Payment gateway reported {ReportedAmount} for payment {PaymentId} of {Amount} ({Gateway})",
                data.Amount, payment.Id, payment.Amount, gateway);
            return GatewayCallbackOutcome.InvalidAmount;
        }

        // A suspicious debit (VNPay 07) is not settled later either: voided like a decline, flagged for staff
        if (!data.IsSuccess)
        {
            if (payment.Status == PaymentStatus.Pending)
            {
                invoice.VoidPayment(payment, Payment.GatewayFailedReason, voidedBy: null, now);
            }

            await RecordCallbackAsync(
                payment.Id, gateway, PaymentTransactionStatus.Failed, data,
                data.IsSuspicious ? SuspiciousDebitError : null, data.IsSuspicious, now, source, ct);

            if (data.IsSuspicious)
            {
                logger.LogError(
                    "Online payment debited but held as suspicious by the gateway, check it with the gateway ({PaymentId}, {Gateway}, transaction {GatewayTransactionId})",
                    payment.Id, gateway, data.GatewayTransactionId);
            }

            return GatewayCallbackOutcome.Received;
        }

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
                invoice.VoidPayment(payment, Payment.GatewayNotAppliedReason, voidedBy: null, now);
                notApplied = ex.Message;
            }
        }

        await RecordCallbackAsync(
            payment.Id, gateway, PaymentTransactionStatus.Success, data, notApplied, notApplied is not null, now, source, ct);

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

    private async Task<bool> IsSignedByAccountAsync(
        IPaymentGateway adapter,
        GatewayCallback callback,
        OrganizationPaymentGateway account,
        Guid paymentId,
        CancellationToken ct)
    {
        if (IsSignedWith(adapter, callback, account.MerchantCode, account.SecretEncrypted))
        {
            return true;
        }

        var checkout = await paymentTransactionRepository.GetLatestCheckoutAsync(paymentId, ct);
        if (checkout?.CredentialsVersion is not { } version || version == account.CredentialsVersion)
        {
            return false;
        }

        var replaced = await organizationPaymentGatewayRepository.GetReplacedCredentialsAsync(account.Id, version, ct);
        return replaced is not null && IsSignedWith(adapter, callback, replaced.MerchantCode, replaced.SecretEncrypted);
    }

    private bool IsSignedWith(IPaymentGateway adapter, GatewayCallback callback, string merchantCode, string secretEncrypted) =>
        adapter.VerifyCallback(callback, new GatewayCredentials(merchantCode, secretProtector.Unprotect(secretEncrypted)));

    private Task RecordCallbackAsync(
        Guid? paymentId,
        PaymentGateway gateway,
        PaymentTransactionStatus status,
        GatewayCallbackData data,
        string? error,
        bool needsReview,
        DateTimeOffset now,
        PaymentTransactionType source,
        CancellationToken cancellationToken) =>
        paymentTransactionRepository.AddAsync(
            PaymentTransaction.Callback(
                paymentId, gateway, status, data.GatewayTransactionId, data.Amount, data.Payload, error, needsReview, now,
                source),
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
                    created.PaymentId, gateway, created.Amount, ex.RequestPayload, ex.ResponsePayload, ex.Message,
                    created.CredentialsVersion, now),
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

    private static MyOnlineCheckoutResponse ToResponse(Guid paymentId, PaymentGateway gateway, PaymentTransaction checkout) => new()
    {
        PaymentId = paymentId,
        Gateway = gateway,
        Amount = checkout.Amount!.Value,
        PaymentUrl = checkout.PaymentUrl!,
        ExpiresAt = checkout.ExpiresAt!.Value
    };

    private abstract record CheckoutStart;

    private sealed record ReusedCheckout(MyOnlineCheckoutResponse Response) : CheckoutStart;

    private sealed record NewCheckout(
        Guid InvoiceId,
        Guid PaymentId,
        decimal Amount,
        string InvoiceNumber,
        DateTimeOffset CreatedAt,
        GatewayCredentials Credentials,
        int CredentialsVersion) : CheckoutStart;
}
