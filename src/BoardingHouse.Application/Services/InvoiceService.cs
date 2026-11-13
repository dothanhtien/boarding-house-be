using Mapster;
using System.Text.Json;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public class InvoiceService(
    ILeaseRepository leaseRepository,
    IRoomRepository roomRepository,
    IInvoiceRepository invoiceRepository,
    InvoiceBillingInputLoader billingInputLoader,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<InvoicePreviewResponse> PreviewAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var lease = await leaseRepository.GetByIdForBillingAsync(request.LeaseId, forUpdate: false, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Lease(request.LeaseId));

        await organizationScopeAccessor.EnsureScopeAsync(
            lease.OrganizationId,
            PermissionResources.Invoice,
            PermissionActions.Create,
            NotFoundMessages.Lease(request.LeaseId),
            cancellationToken);

        var calculation = await CalculateAsync(lease, request, cancellationToken);

        return ToPreviewResponse(lease, request, calculation);
    }

    public async Task<InvoiceDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await invoiceRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            invoice.OrganizationId,
            PermissionResources.Invoice,
            PermissionActions.Read,
            NotFoundMessages.Invoice(id),
            cancellationToken);

        return invoice.Adapt<InvoiceDetailResponse>();
    }

    public async Task<InvoiceDetailResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var billingMonth = BillingMonth.FromFirstDay(request.BillingMonth!.Value);
        Guid invoiceId;

        try
        {
            invoiceId = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var roomId = await leaseRepository.GetRoomIdAsync(request.LeaseId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(request.LeaseId));

                _ = await roomRepository.GetByIdWithOrganizationIdForShareAsync(roomId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(request.LeaseId));

                var lease = await leaseRepository.GetByIdForBillingAsync(request.LeaseId, forUpdate: true, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(request.LeaseId));

                await organizationScopeAccessor.EnsureScopeAsync(
                    lease.OrganizationId,
                    PermissionResources.Invoice,
                    PermissionActions.Create,
                    NotFoundMessages.Lease(request.LeaseId),
                    ct);

                if (lease.RoomId != roomId)
                {
                    throw new AppConflictException("The lease was modified concurrently, please retry");
                }

                var calculation = await CalculateAsync(lease, request, ct);

                if (calculation.Errors.Any(e => e.Code == InvoiceIssueCode.AlreadyInvoiced))
                {
                    throw new AppConflictException($"The lease already has an invoice for {billingMonth}");
                }

                if (!calculation.CanCreate)
                {
                    throw new AppValidationException("The invoice cannot be created", ToErrors(calculation.Errors));
                }

                var number = await invoiceRepository.NextNumberAsync(lease.OrganizationId, billingMonth, ct);

                var invoice = Invoice.Create(
                    calculation,
                    lease.OrganizationId,
                    lease.Id,
                    Invoice.FormatNumber(billingMonth, number),
                    request.DiscountReason,
                    request.Notes,
                    DateTimeOffset.UtcNow,
                    currentUserAccessor.RequiredUser.Id);

                await invoiceRepository.AddAsync(invoice, ct);

                return invoice.Id;
            }, cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.InvoiceLeaseIdBillingMonth)
        {
            logger.LogWarning(
                "Create invoice failed: lease {LeaseId} already has an invoice for {BillingMonth}", request.LeaseId, billingMonth);
            throw new AppConflictException($"The lease already has an invoice for {billingMonth}");
        }

        logger.LogInformation(
            "Invoice created ({InvoiceId}, lease {LeaseId}, month {BillingMonth})", invoiceId, request.LeaseId, billingMonth);

        return await GetDetailAsync(invoiceId, cancellationToken);
    }

    private async Task<InvoiceCalculation> CalculateAsync(
        Lease lease, CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var customLines = (request.CustomItems ?? [])
            .Select(i => InvoiceLine.Custom(i.Description!, i.Quantity, i.UnitPrice!.Value))
            .ToList();

        var input = await billingInputLoader.LoadAsync(
            lease,
            BillingMonth.FromFirstDay(request.BillingMonth!.Value),
            Today,
            customLines,
            request.DiscountAmount,
            cancellationToken);

        return InvoiceCalculator.Calculate(input);
    }

    private static InvoicePreviewResponse ToPreviewResponse(
        Lease lease, CreateInvoiceRequest request, InvoiceCalculation calculation) => new()
        {
            OrganizationId = lease.OrganizationId,
            PropertyId = lease.Room!.PropertyId,
            PropertyName = lease.Room.Property!.Name,
            RoomId = lease.RoomId,
            RoomNumber = lease.Room.RoomNumber,
            LeaseId = lease.Id,
            LeaseCode = lease.LeaseCode,
            BillingMonth = calculation.BillingMonth.FirstDay,
            DueDate = calculation.DueDate,
            Subtotal = calculation.Totals.Subtotal,
            VatRate = calculation.VatRate,
            VatAmount = calculation.Totals.VatAmount,
            DiscountAmount = calculation.DiscountAmount,
            DiscountReason = calculation.DiscountAmount > 0 ? request.DiscountReason?.Trim() : null,
            TotalAmount = calculation.Totals.TotalAmount,
            Items = calculation.Lines
                .Select((line, index) => new InvoiceLineResponse
                {
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Amount = line.Amount,
                    SortOrder = index,
                    SourceType = line.SourceType,
                    SourceId = line.SourceId
                })
                .ToList(),
            Errors = calculation.Errors.Adapt<List<InvoiceIssueResponse>>(),
            Warnings = calculation.Warnings.Adapt<List<InvoiceIssueResponse>>(),
            CanCreate = calculation.CanCreate
        };

    private async Task<InvoiceDetailResponse> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(id));

        return invoice.Adapt<InvoiceDetailResponse>();
    }

    private static Dictionary<string, string[]> ToErrors(IEnumerable<InvoiceIssue> issues) =>
        issues
            .GroupBy(i => JsonNamingPolicy.CamelCase.ConvertName(i.Code.ToString()))
            .ToDictionary(g => g.Key, g => g.Select(i => i.Message).ToArray());
}
