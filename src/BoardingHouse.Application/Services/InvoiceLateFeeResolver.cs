using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public class InvoiceLateFeeResolver(
    IPropertyRepository propertyRepository,
    IOrganizationSettingsRepository organizationSettingsRepository)
{
    public async Task<LateFeePolicy> ResolveAsync(Invoice invoice, CancellationToken cancellationToken) =>
        InvoiceCalculator.ResolveLateFee(
            await propertyRepository.GetByLeaseIdAsync(invoice.LeaseId, cancellationToken),
            await organizationSettingsRepository.GetByOrganizationIdAsync(invoice.OrganizationId, cancellationToken));
}
