using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public class InvoiceService(
    ILeaseRepository leaseRepository,
    InvoiceBillingInputLoader billingInputLoader,
    IOrganizationScopeAccessor organizationScopeAccessor) : IInvoiceService
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
}
