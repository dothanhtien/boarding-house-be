using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Application.Services.Security;
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
