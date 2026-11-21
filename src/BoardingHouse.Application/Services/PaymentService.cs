using System.Linq.Expressions;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
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
    ICurrentOrganizationAccessor currentOrganizationAccessor,
    ILogger<PaymentService> logger) : IPaymentService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly Dictionary<string, Expression<Func<Payment, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = p => p.PaymentDate,
        ["paymentDate"] = p => p.PaymentDate,
        ["amount"] = p => p.Amount,
        ["createdAt"] = p => p.CreatedAt
    };

    public async Task<PagedResult<PaymentResponse>> GetAllAsync(PaymentListQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = query.OrganizationId ?? currentOrganizationAccessor.OrganizationId;

        var scope = await organizationScopeAccessor.GetScopeAsync(
            PermissionResources.Payment, PermissionActions.Read, cancellationToken);

        if (organizationId is not null && !scope.Allows(organizationId.Value))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId.Value));
        }

        var allowedOrganizationIds = organizationId is null && !scope.IsUnrestricted ? scope.OrganizationIds : null;
        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);

        var paged = await paymentRepository.SearchAsync(
            organizationId,
            allowedOrganizationIds,
            query.PropertyId,
            query.Method,
            query.Status,
            query.From is { } from ? StartOfUtcDay(from) : null,
            query.To is { } to && to != DateOnly.MaxValue ? StartOfUtcDay(to.AddDays(1)) : null,
            query.Search,
            query,
            sortField,
            query.SortOrder,
            cancellationToken);

        return new PagedResult<PaymentResponse>
        {
            Items = paged.Items.Adapt<List<PaymentResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<List<PaymentResponse>> GetByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var organizationId = await invoiceRepository.GetOrganizationIdAsync(invoiceId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Payment,
            PermissionActions.Read,
            NotFoundMessages.Invoice(invoiceId),
            cancellationToken);

        var payments = await paymentRepository.ListByInvoiceIdAsync(invoiceId, cancellationToken);

        return payments.Adapt<List<PaymentResponse>>();
    }

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

            var actorId = currentUserAccessor.RequiredUser.Id;
            var now = DateTimeOffset.UtcNow;
            var lateFee = await lateFeeResolver.ResolveAsync(invoice, ct);
            invoice.RefreshStatus(Today, lateFee, actorId, now);

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

            var payment = invoice.RecordPayment(details, actorId, now);
            await paymentRepository.AddAsync(payment, ct);

            if (request.Confirm)
            {
                invoice.ConfirmPayment(payment, actorId, now);
                invoice.RefreshStatus(Today, lateFee, actorId, now);
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
            var actorId = currentUserAccessor.RequiredUser.Id;
            var now = DateTimeOffset.UtcNow;
            var (invoice, payment, lateFee) = await LockAsync(id, PermissionActions.Confirm, actorId, now, ct);

            invoice.ConfirmPayment(payment, actorId, now);
            invoice.RefreshStatus(Today, lateFee, actorId, now);
        }, cancellationToken);

        logger.LogInformation("Payment confirmed ({PaymentId})", id);

        return await GetDetailAsync(id, cancellationToken);
    }

    public async Task<PaymentResponse> VoidAsync(Guid id, VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var actorId = currentUserAccessor.RequiredUser.Id;
            var now = DateTimeOffset.UtcNow;
            var (invoice, payment, lateFee) = await LockAsync(id, PermissionActions.Void, actorId, now, ct);

            invoice.VoidPayment(payment, request.Reason, actorId, now);
            invoice.RefreshStatus(Today, lateFee, actorId, now);
        }, cancellationToken);

        logger.LogInformation("Payment voided ({PaymentId})", id);

        return await GetDetailAsync(id, cancellationToken);
    }

    private async Task<(Invoice Invoice, Payment Payment, LateFeePolicy LateFee)> LockAsync(
        Guid id, string action, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
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

        var lateFee = await lateFeeResolver.ResolveAsync(invoice, cancellationToken);
        invoice.RefreshStatus(Today, lateFee, actorId, now);

        return (invoice, payment, lateFee);
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

    private static DateTimeOffset StartOfUtcDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
