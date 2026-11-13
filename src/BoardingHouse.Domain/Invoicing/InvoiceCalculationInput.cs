using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Domain.Invoicing;

public sealed record InvoiceCalculationInput
{
    // Tenants loaded
    public required Lease Lease { get; init; }
    public required Property Property { get; init; }
    public OrganizationSettings? Settings { get; init; }

    public required BillingMonth BillingMonth { get; init; }
    public required DateOnly Today { get; init; }

    // Every lease_utility_services row of the lease (active or not), Service loaded (soft-deleted services included)
    public required IReadOnlyList<LeaseUtilityService> LeaseServices { get; init; }

    // Readings billed to the lease, ReadingMonth <= BillingMonth, not on a live invoice, Service loaded
    public required IReadOnlyList<UtilityReading> UninvoicedReadings { get; init; }

    // Every live reading of the lease's room for BillingMonth (any billed lease)
    public required IReadOnlyList<UtilityReading> MonthReadings { get; init; }

    public required bool HasDepositItem { get; init; }
    public required bool HasInvoiceForMonth { get; init; }

    // Built with InvoiceLine.Custom
    public IReadOnlyList<InvoiceLine> CustomLines { get; init; } = [];
    public decimal DiscountAmount { get; init; }
}
