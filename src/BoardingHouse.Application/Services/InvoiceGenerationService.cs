using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.Services;

public class InvoiceGenerationService(
    IPropertyRepository propertyRepository,
    ILeaseRepository leaseRepository,
    IInvoiceGenerationRunRepository invoiceGenerationRunRepository,
    IInvoiceGenerator invoiceGenerator,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ICurrentOrganizationAccessor currentOrganizationAccessor) : IInvoiceGenerationService
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

    public async Task<PagedResult<InvoiceGenerationRunResponse>> GetRunsAsync(
        InvoiceGenerationRunListQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = query.OrganizationId ?? currentOrganizationAccessor.OrganizationId;

        var scope = await organizationScopeAccessor.GetScopeAsync(
            PermissionResources.Invoice, PermissionActions.Read, cancellationToken);

        if (organizationId is not null && !scope.Allows(organizationId.Value))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId.Value));
        }

        var allowedOrganizationIds = organizationId is null && !scope.IsUnrestricted ? scope.OrganizationIds : null;

        var paged = await invoiceGenerationRunRepository.SearchAsync(
            organizationId,
            allowedOrganizationIds,
            query.PropertyId,
            query.BillingMonth is { } month ? BillingMonth.FromFirstDay(month) : null,
            query,
            cancellationToken);

        return new PagedResult<InvoiceGenerationRunResponse>
        {
            Items = paged.Items.Adapt<List<InvoiceGenerationRunResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

}
