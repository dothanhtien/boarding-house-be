using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public Guid? RoomId { get; init; }
    public Guid? LeaseId { get; init; }
    public InvoiceStatus? Status { get; init; }
    public DateOnly? BillingMonth { get; init; }
}
