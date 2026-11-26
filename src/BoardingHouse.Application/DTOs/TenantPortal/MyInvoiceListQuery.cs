using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyInvoiceListQuery : PageRequest
{
    public Guid? LeaseId { get; init; }
    public InvoiceStatus? Status { get; init; }
    public DateOnly? BillingMonth { get; init; }
}
