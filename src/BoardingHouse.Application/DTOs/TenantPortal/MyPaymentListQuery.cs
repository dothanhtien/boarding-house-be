using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyPaymentListQuery : PageRequest
{
    public Guid? InvoiceId { get; init; }
    public PaymentStatus? Status { get; init; }
}
