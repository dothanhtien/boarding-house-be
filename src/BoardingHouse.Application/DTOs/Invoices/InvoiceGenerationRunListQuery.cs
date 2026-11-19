using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceGenerationRunListQuery : PageRequest
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public DateOnly? BillingMonth { get; init; }
}
