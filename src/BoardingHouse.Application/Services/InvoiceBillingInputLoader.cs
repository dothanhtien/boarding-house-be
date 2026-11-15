using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public class InvoiceBillingInputLoader(
    ILeaseRepository leaseRepository,
    IOrganizationSettingsRepository organizationSettingsRepository,
    IUtilityReadingRepository utilityReadingRepository,
    IInvoiceRepository invoiceRepository)
{
    // lease from ILeaseRepository.GetByIdForBillingAsync (Room, Room.Property and Tenants loaded)
    public async Task<InvoiceCalculationInput> LoadAsync(
        Lease lease,
        BillingMonth month,
        DateOnly today,
        IReadOnlyList<InvoiceLine> customLines,
        decimal discountAmount,
        CancellationToken cancellationToken)
    {
        var monthReadings = await utilityReadingRepository.ListByRoomAndMonthWithImageAsync(lease.RoomId, month, cancellationToken);

        return new InvoiceCalculationInput
        {
            Lease = lease,
            Property = lease.Room!.Property!,
            Settings = await organizationSettingsRepository.GetByOrganizationIdAsync(lease.OrganizationId, cancellationToken),
            BillingMonth = month,
            Today = today,
            LeaseServices = await leaseRepository.ListUtilityServicesAsync(lease.Id, cancellationToken),
            UninvoicedReadings = await utilityReadingRepository.ListUninvoicedByBilledLeaseIdAsync(lease.Id, month, cancellationToken),
            MonthReadings = monthReadings.Select(r => r.Entity).ToList(),
            HasDepositItem = await invoiceRepository.ExistsLiveDepositItemAsync(lease.Id, cancellationToken),
            HasInvoiceForMonth = await invoiceRepository.ExistsLiveForLeaseMonthAsync(lease.Id, month, cancellationToken),
            CustomLines = customLines,
            DiscountAmount = discountAmount
        };
    }
}
