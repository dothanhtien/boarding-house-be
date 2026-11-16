using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.Services;

public class InvoiceGenerationService(
    IPropertyRepository propertyRepository,
    ILeaseRepository leaseRepository,
    IInvoiceGenerator invoiceGenerator,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor) : IInvoiceGenerationService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<InvoiceGenerationRunResponse> GenerateForPropertyAsync(
        Guid propertyId, GenerateInvoicesRequest request, CancellationToken cancellationToken = default)
    {
        var organizationId = await propertyRepository.GetOrganizationIdAsync(propertyId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Property(propertyId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Invoice,
            PermissionActions.Create,
            NotFoundMessages.Property(propertyId),
            cancellationToken);

        var month = BillingMonth.FromFirstDay(request.BillingMonth!.Value);
        var leases = await leaseRepository.ListToBillByPropertyAsync(propertyId, month, cancellationToken);

        var run = await invoiceGenerator.GenerateAllAsync(
            organizationId, propertyId, month, Today, leases, currentUserAccessor.RequiredUser.Id, cancellationToken);

        return run.Adapt<InvoiceGenerationRunResponse>();
    }
}
